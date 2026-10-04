using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Processing;
using NetVips;

namespace ComicPlatform.Infrastructure.Imaging;

public sealed record PageResult(int Width, int Height, long Size, bool Reencoded);

/// <summary>
/// libvips-based conversion. All methods are synchronous and CPU-bound — call them only from
/// <see cref="ImageWorkerPool"/>. libvips runs single-threaded per image (concurrency = 1) and with
/// its operation cache disabled, so parallelism is controlled solely by the worker pool.
/// </summary>
public sealed class NetVipsImageProcessor
{
    private static readonly object InitLock = new();
    private static bool _initialized;
    private readonly LimitSettings _limits;

    public NetVipsImageProcessor(LimitSettings limits)
    {
        _limits = limits;
        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            NetVips.NetVips.Concurrency = 1;
            Cache.Max = 0;
            Cache.MaxFiles = 0;
            Cache.MaxMem = 0;
            _initialized = true;
        }
    }

    /// <summary>Validates a page image and writes it as WebP (copying already-valid WebP verbatim).</summary>
    public PageResult ConvertPage(string inputPath, ImageFormat format, string outputPath, string displayName)
    {
        try
        {
            int width;
            int height;
            using (var probe = Image.NewFromFile(inputPath, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error))
            {
                width = probe.Width;
                height = probe.Height;
            }

            EnsureDimensions(width, height, displayName);

            var maxWidth = _limits.PageMaxWidth > 0 ? _limits.PageMaxWidth : int.MaxValue;
            if (format == ImageFormat.Webp && width <= maxWidth)
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                return new PageResult(width, height, new FileInfo(outputPath).Length, Reencoded: false);
            }

            using var image = width > maxWidth
                ? Image.Thumbnail(inputPath, maxWidth, height: 10_000_000, size: Enums.Size.Down)
                : Image.NewFromFile(inputPath, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
            using var srgb = ToSrgb(image);
            EnsureDimensions(srgb.Width, srgb.Height, displayName);
            srgb.Webpsave(outputPath, q: _limits.WebpQuality, effort: 4, keep: Enums.ForeignKeep.None);
            return new PageResult(srgb.Width, srgb.Height, new FileInfo(outputPath).Length, Reencoded: true);
        }
        catch (VipsException ex)
        {
            throw new PipelineException("corrupt_image", $"Không đọc được ảnh \"{PipelineException.DisplayName(displayName)}\" (tệp hỏng hoặc định dạng không hỗ trợ).") { Data = { ["vips"] = ex.Message } };
        }
    }

    /// <summary>Cover / logo thumbnail: shrink-on-load to <paramref name="maxWidth"/>, WebP output.</summary>
    public PageResult CreateThumbnail(string inputPath, string outputPath, int maxWidth, int maxHeight)
    {
        try
        {
            int width;
            int height;
            using (var probe = Image.NewFromFile(inputPath, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error))
            {
                width = probe.Width;
                height = probe.Height;
            }

            EnsureDimensions(width, height, "ảnh bìa");
            using var thumb = Image.Thumbnail(inputPath, maxWidth, height: maxHeight, size: Enums.Size.Down);
            using var srgb = ToSrgb(thumb);
            srgb.Webpsave(outputPath, q: 80, effort: 4, keep: Enums.ForeignKeep.None);
            return new PageResult(srgb.Width, srgb.Height, new FileInfo(outputPath).Length, Reencoded: true);
        }
        catch (VipsException)
        {
            throw new PipelineException("corrupt_image", "Không đọc được ảnh (tệp hỏng hoặc định dạng không hỗ trợ).");
        }
    }

    private void EnsureDimensions(int width, int height, string displayName)
    {
        if (width < 1 || height < 1 || width > _limits.MaxImageDimension || height > _limits.MaxImageDimension || (long)width * height > _limits.MaxImagePixels)
        {
            throw new PipelineException("image_too_large",
                $"Ảnh \"{PipelineException.DisplayName(displayName)}\" có kích thước {width}×{height}px, vượt giới hạn ({_limits.MaxImageDimension}px mỗi chiều, {_limits.MaxImagePixels / 1_000_000} megapixel). Hãy cắt nhỏ ảnh.");
        }
    }

    private static Image ToSrgb(Image image)
    {
        var interpretation = image.Interpretation;
        return interpretation is Enums.Interpretation.Srgb or Enums.Interpretation.Bw
            ? image.Copy()
            : image.Colourspace(Enums.Interpretation.Srgb);
    }
}
