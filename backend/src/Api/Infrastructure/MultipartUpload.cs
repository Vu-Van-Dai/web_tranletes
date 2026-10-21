using ComicPlatform.Application.Common;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ComicPlatform.Api.Infrastructure;

/// <summary>
/// Streams the first file part of a multipart/form-data request straight to a handler without
/// IFormFile buffering. Raises the per-request body limit only for the endpoint that calls it.
/// </summary>
public static class MultipartUpload
{
    private const long MultipartOverhead = 1024 * 1024;

    public static async Task<T> ReadFirstFileAsync<T>(HttpContext http, long maxFileBytes, Func<Stream, string?, Task<T>> handler, CancellationToken ct)
    {
        var reader = CreateReader(http, maxFileBytes, maxFileBytes);
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            if (TryGetFile(section, out _, out var fileName))
            {
                return await RunAsync(() => handler(section.Body, fileName), maxFileBytes);
            }
        }

        throw Errors.BadRequest("missing_file", "Không tìm thấy tệp trong yêu cầu tải lên.");
    }

    /// <summary>
    /// Chapter upload: up to <paramref name="maxImages"/> optional image parts (field names in
    /// <paramref name="imageFields"/>, e.g. promotional posters) followed by the archive part. Parts are
    /// streamed in the order they arrive, so the images must be sent before the archive.
    /// </summary>
    public static async Task<T> ReadChapterUploadAsync<T>(
        HttpContext http,
        long maxArchiveBytes,
        long maxImageBytes,
        int maxImages,
        IReadOnlySet<string> imageFields,
        Func<string, Stream, string?, int, Task> onImage,
        Func<Stream, string?, Task<T>> onArchive,
        CancellationToken ct)
    {
        var reader = CreateReader(http, maxArchiveBytes + (maxImages * maxImageBytes), Math.Max(maxArchiveBytes, maxImageBytes));
        var images = 0;
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            if (!TryGetFile(section, out var field, out var fileName))
            {
                continue;
            }

            if (imageFields.Contains(field))
            {
                if (++images > maxImages)
                {
                    throw Errors.BadRequest("too_many_extra_pages", $"Chỉ được kèm tối đa {maxImages} ảnh quảng bá cho mỗi chương.");
                }

                var index = images;
                await RunAsync(async () =>
                {
                    await onImage(field, section.Body, fileName, index);
                    return true;
                }, maxImageBytes);
                continue;
            }

            return await RunAsync(() => onArchive(section.Body, fileName), maxArchiveBytes);
        }

        throw Errors.BadRequest("missing_file", "Không tìm thấy tệp nén chương trong yêu cầu tải lên.");
    }

    private static MultipartReader CreateReader(HttpContext http, long maxTotalFileBytes, long maxPartBytes)
    {
        var limit = maxTotalFileBytes + MultipartOverhead;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        if (http.Request.ContentLength is { } length && length > limit)
        {
            throw Errors.PayloadTooLarge($"Tệp tải lên vượt quá giới hạn {maxTotalFileBytes / (1024 * 1024)} MB.");
        }

        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw Errors.BadRequest("multipart_required", "Yêu cầu phải ở dạng multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 200)
        {
            throw Errors.BadRequest("multipart_required", "Thiếu boundary của multipart.");
        }

        return new MultipartReader(boundary, http.Request.Body)
        {
            HeadersLengthLimit = 16 * 1024,
            HeadersCountLimit = 16,
            BodyLengthLimit = maxPartBytes + 1,
        };
    }

    private static bool TryGetFile(MultipartSection section, out string field, out string? fileName)
    {
        field = "";
        fileName = null;
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.DispositionType.Equals("form-data")
            || (Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition!.FileName)
                && Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition.FileNameStar)))
        {
            return false;
        }

        field = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
        fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
        return true;
    }

    private static async Task<T> RunAsync<T>(Func<Task<T>> handler, long maxFileBytes)
    {
        try
        {
            return await handler();
        }
        catch (InvalidDataException)
        {
            // MultipartReader enforces BodyLengthLimit by throwing InvalidDataException.
            throw Errors.PayloadTooLarge($"Tệp tải lên vượt quá giới hạn {maxFileBytes / (1024 * 1024)} MB.");
        }
    }
}
