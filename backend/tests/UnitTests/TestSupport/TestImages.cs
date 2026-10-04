using System.IO.Compression;
using ComicPlatform.Infrastructure.Imaging;
using NetVips;

namespace ComicPlatform.UnitTests.TestSupport;

public static class TestImages
{
    static TestImages() => NetVipsImageProcessor.EnsureInitialized();

    public static byte[] Png(int width = 80, int height = 120, int shade = 120) => Solid(width, height, shade).PngsaveBuffer();

    public static byte[] Jpeg(int width = 80, int height = 120, int shade = 90) => Solid(width, height, shade).JpegsaveBuffer(q: 85);

    public static byte[] Webp(int width = 80, int height = 120, int shade = 60) => Solid(width, height, shade).WebpsaveBuffer(q: 80);

    private static Image Solid(int width, int height, int shade)
    {
        using var black = Image.Black(width, height, bands: 3);
        return (black + new double[] { shade, 255 - shade, (shade * 3) % 255 }).Cast(Enums.BandFormat.Uchar);
    }
}

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Zips
{
    public static byte[] Build(params (string Name, byte[] Data)[] entries) => Build(CompressionLevel.Optimal, entries);

    public static byte[] Build(CompressionLevel level, params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name, level);
                using var s = entry.Open();
                s.Write(data);
            }
        }

        return ms.ToArray();
    }

    /// <summary>Creates a zip whose entry is a Unix symlink (mode 0xA1FF in external attributes).</summary>
    public static byte[] BuildWithSymlink(string name)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
            using var s = entry.Open();
            s.Write("../../etc/passwd"u8);
        }

        return ms.ToArray();
    }

    public static ZipArchive Open(byte[] data) => new(new MemoryStream(data), ZipArchiveMode.Read);
}
