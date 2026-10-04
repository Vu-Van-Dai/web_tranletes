using System.IO.Compression;
using ComicPlatform.Infrastructure.Archives;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Imaging;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.UnitTests.TestSupport;

namespace ComicPlatform.UnitTests.Media;

public class SafeZipTests
{
    private static readonly ArchiveLimits Limits = new(MaxPages: 5, MaxExtractedSize: 5 * 1024 * 1024, MaxEntrySize: 2 * 1024 * 1024, MaxCompressionRatio: 100);

    private static PipelineException Rejects(byte[] zip, ArchiveLimits? limits = null)
    {
        using var archive = Zips.Open(zip);
        return Assert.Throws<PipelineException>(() => SafeZipReader.ValidateAndOrder(archive, limits ?? Limits));
    }

    [Theory]
    [InlineData("../evil.jpg")]
    [InlineData("pages/../../evil.jpg")]
    [InlineData("/etc/evil.jpg")]
    [InlineData("C:/Windows/evil.jpg")]
    [InlineData("..\\evil.jpg")]
    public void Zip_slip_names_are_rejected(string name)
    {
        var ex = Rejects(Zips.Build((name, TestImages.Png())));
        Assert.Equal("unsafe_path", ex.Code);
    }

    [Fact]
    public void Symlink_entries_are_rejected() =>
        Assert.Equal("symlink_entry", Rejects(Zips.BuildWithSymlink("page1.jpg")).Code);

    [Fact]
    public void Junk_is_ignored_and_pages_are_naturally_ordered()
    {
        var zip = Zips.Build(
            ("chap/10.png", TestImages.Png()),
            ("chap/2.png", TestImages.Png()),
            ("chap/1.jpg", TestImages.Jpeg()),
            ("__MACOSX/chap/._1.jpg", [1, 2, 3]),
            ("chap/.hidden.png", TestImages.Png()),
            ("Thumbs.db", [0]),
            ("credits.txt", "Nhóm dịch ABC"u8.ToArray()),
            ("ComicInfo.xml", "<x/>"u8.ToArray()));
        using var archive = Zips.Open(zip);
        var pages = SafeZipReader.ValidateAndOrder(archive, Limits);
        Assert.Equal(["chap/1.jpg", "chap/2.png", "chap/10.png"], pages.Select(p => p.FullName));
    }

    [Fact]
    public void Too_many_pages_and_empty_archives_are_rejected()
    {
        var six = Enumerable.Range(1, 6).Select(i => ($"{i}.png", TestImages.Png())).ToArray();
        Assert.Equal("too_many_pages", Rejects(Zips.Build(six)).Code);
        Assert.Equal("archive_empty", Rejects(Zips.Build(("readme.txt", "x"u8.ToArray()))).Code);
    }

    [Fact]
    public void Decompression_bomb_ratio_is_rejected()
    {
        var bomb = new byte[3 * 1024 * 1024]; // zeros compress ~1000:1
        var ex = Rejects(Zips.Build(CompressionLevel.SmallestSize, ("bomb.png", bomb)), Limits with { MaxEntrySize = 10 * 1024 * 1024 });
        Assert.Equal("suspicious_compression", ex.Code);
    }

    [Fact]
    public void Oversized_entries_and_totals_are_rejected()
    {
        var big = new byte[3 * 1024 * 1024];
        Random.Shared.NextBytes(big);
        Assert.Equal("image_file_too_large", Rejects(Zips.Build(CompressionLevel.NoCompression, ("big.jpg", big))).Code);

        var limits = Limits with { MaxExtractedSize = 4 * 1024 * 1024, MaxEntrySize = 3 * 1024 * 1024 };
        Assert.Equal("archive_too_large", Rejects(Zips.Build(CompressionLevel.NoCompression, ("a.jpg", big), ("b.jpg", big)), limits).Code);
    }

    [Fact]
    public async Task Extraction_enforces_the_real_byte_cap()
    {
        using var dir = new TempDir();
        var data = new byte[64 * 1024];
        Random.Shared.NextBytes(data);
        using var archive = Zips.Open(Zips.Build(CompressionLevel.NoCompression, ("p.jpg", data)));
        var entry = archive.Entries[0];
        var ex = await Assert.ThrowsAsync<PipelineException>(() => SafeZipReader.ExtractAsync(entry, dir.File("out.bin"), maxBytes: 1024, CancellationToken.None));
        Assert.Equal("suspicious_compression", ex.Code);
    }
}

public class ImageTests
{
    [Fact]
    public void Signatures_are_detected_from_content_not_extension()
    {
        Assert.Equal(ImageFormat.Png, ImageSignature.Detect(TestImages.Png()));
        Assert.Equal(ImageFormat.Jpeg, ImageSignature.Detect(TestImages.Jpeg()));
        Assert.Equal(ImageFormat.Webp, ImageSignature.Detect(TestImages.Webp()));
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect("MZ\x90\0this is a windows exe"u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect("<svg xmlns='http://www.w3.org/2000/svg'/>"u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect("GIF89a....."u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect([]));
    }

    private static NetVipsImageProcessor Processor(int maxWidth = 2000, long maxPixels = 40_000_000) => new(new LimitSettings
    {
        MaxImageDimension = 16_383,
        MaxImagePixels = maxPixels,
        PageMaxWidth = maxWidth,
        WebpQuality = 80,
    });

    [Fact]
    public void Png_and_jpeg_are_converted_to_webp()
    {
        using var dir = new TempDir();
        foreach (var (format, bytes) in new[] { (ImageFormat.Png, TestImages.Png(300, 500)), (ImageFormat.Jpeg, TestImages.Jpeg(300, 500)) })
        {
            var input = dir.File($"in-{format}");
            var output = dir.File($"out-{format}.webp");
            File.WriteAllBytes(input, bytes);
            var result = Processor().ConvertPage(input, format, output, "trang.png");
            Assert.True(result.Reencoded);
            Assert.Equal((300, 500), (result.Width, result.Height));
            Assert.Equal(ImageFormat.Webp, ImageSignature.Detect(File.ReadAllBytes(output)));
        }
    }

    [Fact]
    public void Valid_webp_is_copied_verbatim_and_wide_images_are_downscaled()
    {
        using var dir = new TempDir();
        var webp = TestImages.Webp(200, 300);
        File.WriteAllBytes(dir.File("a.webp"), webp);
        var copied = Processor().ConvertPage(dir.File("a.webp"), ImageFormat.Webp, dir.File("a-out.webp"), "a.webp");
        Assert.False(copied.Reencoded);
        Assert.Equal(webp, File.ReadAllBytes(dir.File("a-out.webp")));

        File.WriteAllBytes(dir.File("wide.png"), TestImages.Png(1000, 400));
        var scaled = Processor(maxWidth: 500).ConvertPage(dir.File("wide.png"), ImageFormat.Png, dir.File("wide.webp"), "wide.png");
        Assert.Equal(500, scaled.Width);
        Assert.Equal(200, scaled.Height);
    }

    [Fact]
    public void Images_over_the_pixel_limit_are_rejected_before_decoding()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("big.png"), TestImages.Png(400, 400));
        var ex = Assert.Throws<PipelineException>(() => Processor(maxPixels: 100_000).ConvertPage(dir.File("big.png"), ImageFormat.Png, dir.File("o.webp"), "big.png"));
        Assert.Equal("image_too_large", ex.Code);
    }

    [Fact]
    public void Corrupt_images_are_rejected()
    {
        using var dir = new TempDir();
        var png = TestImages.Png(200, 200);
        File.WriteAllBytes(dir.File("bad.png"), png[..40]);
        var ex = Assert.Throws<PipelineException>(() => Processor().ConvertPage(dir.File("bad.png"), ImageFormat.Png, dir.File("o.webp"), "bad.png"));
        Assert.Equal("corrupt_image", ex.Code);
    }
}
