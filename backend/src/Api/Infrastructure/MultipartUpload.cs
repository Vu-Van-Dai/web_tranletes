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
        var limit = maxFileBytes + MultipartOverhead;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        if (http.Request.ContentLength is { } length && length > limit)
        {
            throw Errors.PayloadTooLarge($"Tệp tải lên vượt quá giới hạn {maxFileBytes / (1024 * 1024)} MB.");
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

        var reader = new MultipartReader(boundary, http.Request.Body)
        {
            HeadersLengthLimit = 16 * 1024,
            HeadersCountLimit = 16,
            BodyLengthLimit = maxFileBytes + 1,
        };

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                && disposition.DispositionType.Equals("form-data")
                && (!Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition!.FileName)
                    || !Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition.FileNameStar)))
            {
                var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                try
                {
                    return await handler(section.Body, fileName);
                }
                catch (InvalidDataException)
                {
                    // MultipartReader enforces BodyLengthLimit by throwing InvalidDataException.
                    throw Errors.PayloadTooLarge($"Tệp tải lên vượt quá giới hạn {maxFileBytes / (1024 * 1024)} MB.");
                }
            }
        }

        throw Errors.BadRequest("missing_file", "Không tìm thấy tệp trong yêu cầu tải lên.");
    }
}
