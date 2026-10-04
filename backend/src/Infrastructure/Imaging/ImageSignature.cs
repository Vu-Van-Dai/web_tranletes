namespace ComicPlatform.Infrastructure.Imaging;

public enum ImageFormat
{
    Unknown = 0,
    Jpeg,
    Png,
    Webp,
}

/// <summary>Detects image formats from magic bytes — file extensions and client MIME types are never trusted.</summary>
public static class ImageSignature
{
    public const int HeaderLength = 32;

    public static ImageFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ImageFormat.Png;
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageFormat.Webp;
        }

        return ImageFormat.Unknown;
    }

    /// <summary>Animated WebP: extended format (VP8X) with the animation flag set.</summary>
    public static bool IsAnimatedWebp(ReadOnlySpan<byte> header) =>
        header.Length >= 21 && header[12..16].SequenceEqual("VP8X"u8) && (header[20] & 0x02) != 0;

    public static async Task<(ImageFormat Format, bool Animated)> DetectFileAsync(string path, CancellationToken ct)
    {
        var buffer = new byte[HeaderLength];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var read = await fs.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        var span = buffer.AsSpan(0, read);
        var format = Detect(span);
        return (format, format == ImageFormat.Webp && IsAnimatedWebp(span));
    }
}
