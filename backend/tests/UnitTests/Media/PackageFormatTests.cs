using System.Buffers.Binary;
using System.Security.Cryptography;
using ComicPlatform.Infrastructure.Packaging;
using ComicPlatform.UnitTests.TestSupport;

namespace ComicPlatform.UnitTests.Media;

public class PackageFormatTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private List<PackagePageSource> Pages(int count)
    {
        var list = new List<PackagePageSource>();
        for (var i = 1; i <= count; i++)
        {
            var bytes = TestImages.Webp(40 + i, 60 + i, i * 20);
            var path = _dir.File($"{i:D3}.webp");
            File.WriteAllBytes(path, bytes);
            list.Add(new PackagePageSource(i, path, bytes.Length, 40 + i, 60 + i));
        }

        return list;
    }

    private async Task<(byte[] Bytes, PackageWriteResult Result, List<PackagePageSource> Pages)> BuildAsync(int count)
    {
        var pages = Pages(count);
        using var ms = new MemoryStream();
        var result = await PackageWriter.WriteAsync(pages, ms, CancellationToken.None);
        return (ms.ToArray(), result, pages);
    }

    [Fact]
    public async Task Round_trip_preserves_raw_page_bytes_and_metadata()
    {
        var (bytes, result, pages) = await BuildAsync(5);
        Assert.Equal(bytes.Length, result.FileSize);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32))), result.Sha256Hex);

        using var ms = new MemoryStream(bytes);
        var info = await PackageReader.ReadAsync(ms, 200, verifyChecksum: true, CancellationToken.None);
        Assert.Equal(5, info.Entries.Count);
        Assert.Equal(result.Sha256Hex, info.TrailerSha256Hex);
        for (var i = 0; i < pages.Count; i++)
        {
            var e = info.Entries[i];
            Assert.Equal(i + 1, e.PageNumber);
            Assert.Equal(pages[i].Width, e.Width);
            Assert.Equal(pages[i].Height, e.Height);
            var raw = bytes.AsSpan((int)(info.DataStart + e.Offset), e.Size).ToArray();
            Assert.Equal(File.ReadAllBytes(pages[i].FilePath), raw); // not re-encoded
        }
    }

    [Fact]
    public async Task Header_layout_matches_spec()
    {
        var (bytes, _, _) = await BuildAsync(2);
        Assert.Equal("CPK1"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(48u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
    }

    [Fact]
    public async Task Tampered_data_fails_checksum()
    {
        var (bytes, _, _) = await BuildAsync(3);
        bytes[^40] ^= 0xFF; // inside the data section
        using var ms = new MemoryStream(bytes);
        await Assert.ThrowsAsync<PackageFormatException>(() => PackageReader.ReadAsync(ms, 200, verifyChecksum: true, CancellationToken.None));
    }

    [Fact]
    public async Task Truncated_package_is_rejected()
    {
        var (bytes, _, _) = await BuildAsync(3);
        using var ms = new MemoryStream(bytes[..^10]);
        await Assert.ThrowsAsync<PackageFormatException>(() => PackageReader.ReadAsync(ms, 200, verifyChecksum: false, CancellationToken.None));
    }

    [Fact]
    public async Task Bad_magic_version_and_page_limit_are_rejected()
    {
        var (bytes, _, _) = await BuildAsync(3);

        var badMagic = (byte[])bytes.Clone();
        badMagic[0] = (byte)'X';
        await AssertInvalid(badMagic, 200);

        var badVersion = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badVersion.AsSpan(4), 9);
        await AssertInvalid(badVersion, 200);

        await AssertInvalid(bytes, maxPages: 2);
    }

    [Fact]
    public async Task Out_of_bounds_or_overlapping_entries_are_rejected()
    {
        var (bytes, _, _) = await BuildAsync(3);

        var overlap = (byte[])bytes.Clone();
        // second entry offset := 0 (overlaps the first page)
        BinaryPrimitives.WriteUInt64LittleEndian(overlap.AsSpan(32 + 24 + 4), 0);
        await AssertInvalid(overlap, 200);

        var huge = (byte[])bytes.Clone();
        // first entry size := uint.MaxValue
        BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(32 + 12), uint.MaxValue);
        await AssertInvalid(huge, 200);

        var badType = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badType.AsSpan(32 + 16), 7);
        await AssertInvalid(badType, 200);
    }

    [Fact]
    public async Task Writer_rejects_non_contiguous_pages()
    {
        var pages = Pages(2);
        pages[1] = pages[1] with { PageNumber = 5 };
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<PackageFormatException>(() => PackageWriter.WriteAsync(pages, ms, CancellationToken.None));
    }

    /// <summary>Emits the golden fixture consumed by the TypeScript parser tests (cross-language parity).</summary>
    [Fact]
    public async Task Golden_fixture_is_up_to_date()
    {
        var fixtureDir = FindRepoPath("frontend", "tests", "fixtures");
        if (fixtureDir is null)
        {
            return;
        }

        var pages = new List<PackagePageSource>();
        for (var i = 1; i <= 3; i++)
        {
            var path = _dir.File($"g{i}.bin");
            var data = Enumerable.Range(0, 10 * i).Select(b => (byte)(b + i)).ToArray();
            File.WriteAllBytes(path, data);
            pages.Add(new PackagePageSource(i, path, data.Length, 100 * i, 150 * i));
        }

        Directory.CreateDirectory(fixtureDir);
        await using (var fs = File.Create(Path.Combine(fixtureDir, "golden-3pages.pkg")))
        {
            var result = await PackageWriter.WriteAsync(pages, fs, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(fixtureDir, "golden-3pages.json"),
                $$"""{ "sha256": "{{result.Sha256Hex}}", "size": {{result.FileSize}}, "pages": [{"n":1,"w":100,"h":150,"size":10},{"n":2,"w":200,"h":300,"size":20},{"n":3,"w":300,"h":450,"size":30}] }""");
        }
    }

    private static async Task AssertInvalid(byte[] bytes, int maxPages)
    {
        using var ms = new MemoryStream(bytes);
        await Assert.ThrowsAsync<PackageFormatException>(() => PackageReader.ReadAsync(ms, maxPages, verifyChecksum: false, CancellationToken.None));
    }

    private static string? FindRepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".gitignore")))
        {
            dir = dir.Parent;
        }

        return dir is null ? null : Path.Combine([dir.FullName, .. parts]);
    }
}
