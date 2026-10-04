using System.IO.Compression;
using ComicPlatform.Domain.Common;
using ComicPlatform.Infrastructure.Processing;

namespace ComicPlatform.Infrastructure.Archives;

public sealed record ArchiveLimits(int MaxPages, long MaxExtractedSize, long MaxEntrySize, int MaxCompressionRatio)
{
    /// <summary>Hard cap on total entries (pages + ignorable junk) to bound central-directory work.</summary>
    public int MaxEntries => Math.Min(5000, (MaxPages * 2) + 100);
}

/// <summary>
/// Validates ZIP/CBZ archives before anything is extracted. Entry names are only used for ordering and
/// messages — extraction always writes to server-generated file names — but unsafe names are still
/// rejected outright (defense in depth against zip-slip).
/// </summary>
public static class SafeZipReader
{
    private static readonly string[] PageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] IgnoredNames = ["thumbs.db", "desktop.ini", ".ds_store"];

    public static IReadOnlyList<ZipArchiveEntry> ValidateAndOrder(ZipArchive archive, ArchiveLimits limits)
    {
        var entries = archive.Entries;
        if (entries.Count > limits.MaxEntries)
        {
            throw new PipelineException("too_many_entries", $"Tệp nén có quá nhiều tệp ({entries.Count}).");
        }

        var pages = new List<ZipArchiveEntry>();
        long declaredTotal = 0;
        foreach (var entry in entries)
        {
            var name = entry.FullName;
            EnsureSafeName(name);

            var isDirectory = name.EndsWith('/') || name.EndsWith('\\');
            if (isDirectory)
            {
                if (entry.Length != 0)
                {
                    throw new PipelineException("unsafe_path", $"Mục thư mục không hợp lệ trong tệp nén: {PipelineException.DisplayName(name)}");
                }

                continue;
            }

            // Unix symlinks (mode 0xA000 in the high 16 bits of external attributes).
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
            {
                throw new PipelineException("symlink_entry", $"Tệp nén chứa liên kết tượng trưng không được hỗ trợ: {PipelineException.DisplayName(name)}");
            }

            if (entry.IsEncrypted)
            {
                throw new PipelineException("encrypted_entry", "Tệp nén có đặt mật khẩu, hệ thống không hỗ trợ.");
            }

            if (IsIgnored(name))
            {
                continue;
            }

            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (!PageExtensions.Contains(extension))
            {
                continue; // credits.txt, ComicInfo.xml, …
            }

            if (entry.Length <= 0)
            {
                throw new PipelineException("unsupported_image", $"Ảnh \"{PipelineException.DisplayName(name)}\" rỗng.");
            }

            if (entry.Length > limits.MaxEntrySize)
            {
                throw new PipelineException("image_file_too_large", $"Ảnh \"{PipelineException.DisplayName(name)}\" vượt quá {limits.MaxEntrySize / (1024 * 1024)} MB.");
            }

            if (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > limits.MaxCompressionRatio)
            {
                throw new PipelineException("suspicious_compression", "Tệp nén có tỉ lệ nén bất thường (nghi ngờ bom giải nén).");
            }

            declaredTotal += entry.Length;
            if (declaredTotal > limits.MaxExtractedSize)
            {
                throw new PipelineException("archive_too_large", $"Tổng dung lượng ảnh sau giải nén vượt quá {limits.MaxExtractedSize / (1024 * 1024)} MB.");
            }

            pages.Add(entry);
        }

        if (pages.Count == 0)
        {
            throw new PipelineException("archive_empty", "Không tìm thấy ảnh JPEG/PNG/WebP nào trong tệp nén.");
        }

        if (pages.Count > limits.MaxPages)
        {
            throw new PipelineException("too_many_pages", $"Chương có {pages.Count} trang, vượt quá giới hạn {limits.MaxPages} trang.");
        }

        return pages.OrderBy(e => e.FullName.Replace('\\', '/'), NaturalStringComparer.Instance).ToList();
    }

    /// <summary>
    /// Copies an entry to <paramref name="destinationPath"/> counting the real bytes: a header that
    /// under-reports the uncompressed size cannot be used to bypass the limits.
    /// </summary>
    public static async Task<long> ExtractAsync(ZipArchiveEntry entry, string destinationPath, long maxBytes, CancellationToken ct)
    {
        await using var input = await entry.OpenAsync(ct);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous);
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes || total > entry.Length)
            {
                throw new PipelineException("suspicious_compression", $"Ảnh \"{PipelineException.DisplayName(entry.FullName)}\" có kích thước thực tế khác khai báo (nghi ngờ bom giải nén).");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    internal static void EnsureSafeName(string name)
    {
        var unsafeName =
            name.Length == 0 || name.Length > 512
            || name.Contains('\0', StringComparison.Ordinal)
            || name.StartsWith('/') || name.StartsWith('\\')
            || name.Contains(':', StringComparison.Ordinal)
            || name.Replace('\\', '/').Split('/').Any(segment => segment == "..");
        if (unsafeName)
        {
            throw new PipelineException("unsafe_path", $"Tệp nén chứa đường dẫn không an toàn: {PipelineException.DisplayName(name)}");
        }
    }

    private static bool IsIgnored(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (normalized.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalized.Split('/');
        if (segments.Any(s => s.StartsWith('.')))
        {
            return true;
        }

        return IgnoredNames.Contains(segments[^1].ToLowerInvariant());
    }
}
