using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ComicPlatform.Infrastructure.Packaging;

/// <summary>
/// CPK1 chapter package (all integers little-endian):
/// <code>
/// Header 32 B : "CPK1" | u16 formatVersion | u16 headerSize | u32 pageCount | u32 indexSize
///               | u64 dataSize | u32 flags | u32 reserved
/// Index 24 B/page : u32 pageNumber | u64 offset (from data start) | u32 size
///                   | u16 contentType | u16 reserved | u16 width | u16 height
/// Data    : raw page bytes, contiguous, in page order (never re-encoded)
/// Trailer : 32 B SHA-256 over header + index + data
/// </code>
/// </summary>
public static class Cpk1
{
    public static ReadOnlySpan<byte> Magic => "CPK1"u8;
    public const ushort FormatVersion = 1;
    public const int HeaderSize = 32;
    public const int IndexEntrySize = 24;
    public const int TrailerSize = 32;
    public const ushort ContentTypeWebp = 1;

    public static long TotalSize(int pageCount, long dataSize) => HeaderSize + ((long)pageCount * IndexEntrySize) + dataSize + TrailerSize;
}

public sealed record PackagePageSource(int PageNumber, string FilePath, long Size, int Width, int Height);

public sealed record PackageWriteResult(long FileSize, string Sha256Hex);

public sealed record PackageEntry(int PageNumber, long Offset, int Size, ushort ContentType, int Width, int Height);

public sealed record PackageInfo(ushort FormatVersion, int HeaderSize, long DataStart, long DataSize, IReadOnlyList<PackageEntry> Entries, string TrailerSha256Hex);

public sealed class PackageFormatException(string message) : Exception(message);

public static class PackageWriter
{
    private const int BufferSize = 81_920;

    /// <summary>
    /// Streams page files into <paramref name="output"/>. Memory use is one copy buffer
    /// regardless of chapter size. Each source file must still have exactly the declared size.
    /// </summary>
    public static async Task<PackageWriteResult> WriteAsync(IReadOnlyList<PackagePageSource> pages, Stream output, CancellationToken ct)
    {
        Validate(pages);

        long dataSize = 0;
        foreach (var p in pages)
        {
            dataSize += p.Size;
        }

        var head = new byte[Cpk1.HeaderSize + (pages.Count * Cpk1.IndexEntrySize)];
        WriteHeader(head, pages.Count, dataSize);
        long offset = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            var e = head.AsSpan(Cpk1.HeaderSize + (i * Cpk1.IndexEntrySize), Cpk1.IndexEntrySize);
            var p = pages[i];
            BinaryPrimitives.WriteUInt32LittleEndian(e[0..], (uint)p.PageNumber);
            BinaryPrimitives.WriteUInt64LittleEndian(e[4..], (ulong)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(e[12..], (uint)p.Size);
            BinaryPrimitives.WriteUInt16LittleEndian(e[16..], Cpk1.ContentTypeWebp);
            BinaryPrimitives.WriteUInt16LittleEndian(e[18..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(e[20..], (ushort)p.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(e[22..], (ushort)p.Height);
            offset += p.Size;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(head);
        await output.WriteAsync(head, ct);

        var buffer = new byte[BufferSize];
        foreach (var p in pages)
        {
            await using var input = new FileStream(p.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length != p.Size)
            {
                throw new PackageFormatException($"Page {p.PageNumber} changed size while packaging.");
            }

            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
            }

            if (copied != p.Size)
            {
                throw new PackageFormatException($"Page {p.PageNumber} was truncated while packaging.");
            }
        }

        var digest = hash.GetHashAndReset();
        await output.WriteAsync(digest, ct);
        await output.FlushAsync(ct);
        return new PackageWriteResult(Cpk1.TotalSize(pages.Count, dataSize), Convert.ToHexStringLower(digest));
    }

    private static void Validate(IReadOnlyList<PackagePageSource> pages)
    {
        if (pages.Count == 0)
        {
            throw new PackageFormatException("A package needs at least one page.");
        }

        for (var i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            if (p.PageNumber != i + 1)
            {
                throw new PackageFormatException("Page numbers must be contiguous and start at 1.");
            }

            if (p.Size is <= 0 or > uint.MaxValue)
            {
                throw new PackageFormatException($"Page {p.PageNumber} has an invalid size.");
            }

            if (p.Width is < 1 or > ushort.MaxValue || p.Height is < 1 or > ushort.MaxValue)
            {
                throw new PackageFormatException($"Page {p.PageNumber} has invalid dimensions.");
            }
        }
    }

    private static void WriteHeader(Span<byte> h, int pageCount, long dataSize)
    {
        Cpk1.Magic.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], Cpk1.FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], Cpk1.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], (uint)pageCount);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], (uint)(pageCount * Cpk1.IndexEntrySize));
        BinaryPrimitives.WriteUInt64LittleEndian(h[16..], (ulong)dataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[24..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[28..], 0);
    }
}

public static class PackageReader
{
    /// <summary>
    /// Parses and validates the header and index (and optionally the SHA-256 trailer) of a package
    /// stream. Never trusts declared sizes beyond the actual stream length.
    /// </summary>
    public static async Task<PackageInfo> ReadAsync(Stream stream, int maxPages, bool verifyChecksum, CancellationToken ct)
    {
        if (!stream.CanSeek)
        {
            throw new ArgumentException("A seekable stream is required.", nameof(stream));
        }

        var length = stream.Length;
        if (length < Cpk1.HeaderSize + Cpk1.TrailerSize)
        {
            throw new PackageFormatException("Package is too small.");
        }

        stream.Position = 0;
        var header = new byte[Cpk1.HeaderSize];
        await stream.ReadExactlyAsync(header, ct);
        var (version, headerSize, pageCount, indexSize, dataSize) = ParseHeader(header, maxPages);

        var dataStart = (long)headerSize + indexSize;
        if (dataStart + (long)dataSize + Cpk1.TrailerSize != length)
        {
            throw new PackageFormatException("Declared sizes do not match the package length.");
        }

        stream.Position = headerSize;
        var index = new byte[indexSize];
        await stream.ReadExactlyAsync(index, ct);
        var entries = ParseIndex(index, pageCount, (long)dataSize);

        stream.Position = length - Cpk1.TrailerSize;
        var trailer = new byte[Cpk1.TrailerSize];
        await stream.ReadExactlyAsync(trailer, ct);

        if (verifyChecksum)
        {
            stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81_920];
            var remaining = length - Cpk1.TrailerSize;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                if (read == 0)
                {
                    throw new PackageFormatException("Unexpected end of package.");
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), trailer))
            {
                throw new PackageFormatException("Package checksum mismatch.");
            }
        }

        return new PackageInfo(version, headerSize, dataStart, (long)dataSize, entries, Convert.ToHexStringLower(trailer));
    }

    private static (ushort Version, int HeaderSize, int PageCount, int IndexSize, ulong DataSize) ParseHeader(ReadOnlySpan<byte> h, int maxPages)
    {
        if (!h[..4].SequenceEqual(Cpk1.Magic))
        {
            throw new PackageFormatException("Not a CPK1 package (bad magic).");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(h[4..]);
        if (version != Cpk1.FormatVersion)
        {
            throw new PackageFormatException($"Unsupported package format version {version}.");
        }

        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(h[6..]);
        if (headerSize < Cpk1.HeaderSize)
        {
            throw new PackageFormatException("Invalid header size.");
        }

        var pageCount = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
        if (pageCount == 0 || pageCount > maxPages)
        {
            throw new PackageFormatException("Invalid page count.");
        }

        var indexSize = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        if (indexSize != pageCount * Cpk1.IndexEntrySize)
        {
            throw new PackageFormatException("Index size does not match page count.");
        }

        var dataSize = BinaryPrimitives.ReadUInt64LittleEndian(h[16..]);
        if (dataSize == 0 || dataSize > long.MaxValue / 2)
        {
            throw new PackageFormatException("Invalid data size.");
        }

        return (version, headerSize, (int)pageCount, (int)indexSize, dataSize);
    }

    private static List<PackageEntry> ParseIndex(ReadOnlySpan<byte> index, int pageCount, long dataSize)
    {
        var entries = new List<PackageEntry>(pageCount);
        long expectedOffset = 0;
        for (var i = 0; i < pageCount; i++)
        {
            var e = index.Slice(i * Cpk1.IndexEntrySize, Cpk1.IndexEntrySize);
            var pageNumber = BinaryPrimitives.ReadUInt32LittleEndian(e);
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(e[4..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
            var contentType = BinaryPrimitives.ReadUInt16LittleEndian(e[16..]);
            var width = BinaryPrimitives.ReadUInt16LittleEndian(e[20..]);
            var height = BinaryPrimitives.ReadUInt16LittleEndian(e[22..]);

            if (pageNumber != i + 1)
            {
                throw new PackageFormatException("Page numbers must be contiguous and start at 1.");
            }

            if (offset != (ulong)expectedOffset || size == 0 || (ulong)expectedOffset + size > (ulong)dataSize)
            {
                throw new PackageFormatException($"Page {pageNumber} is out of bounds or overlapping.");
            }

            if (contentType != Cpk1.ContentTypeWebp)
            {
                throw new PackageFormatException($"Page {pageNumber} has an unknown content type.");
            }

            if (width == 0 || height == 0)
            {
                throw new PackageFormatException($"Page {pageNumber} has invalid dimensions.");
            }

            entries.Add(new PackageEntry((int)pageNumber, (long)offset, (int)size, contentType, width, height));
            expectedOffset += size;
        }

        if (expectedOffset != dataSize)
        {
            throw new PackageFormatException("Data section has trailing or missing bytes.");
        }

        return entries;
    }
}
