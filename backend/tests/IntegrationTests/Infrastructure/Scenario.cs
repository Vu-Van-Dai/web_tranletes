using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ComicPlatform.Infrastructure.Imaging;
using NetVips;

namespace ComicPlatform.IntegrationTests.Infrastructure;

/// <summary>Builds realistic state through the public HTTP API (never by writing to the database).</summary>
public sealed class Scenario(ApiFactory factory)
{
    public ApiClient Anonymous() => new(factory.CreateClient());

    public async Task<ApiClient> LoginAsync(string login, string password)
    {
        var client = Anonymous();
        var session = await client.PostAsync("/api/auth/login", new { login, password });
        client.Token = session.Str("accessToken");
        client.UserId = session.GetProperty("user").Id();
        client.Username = session.GetProperty("user").Str("username");
        return client;
    }

    public Task<ApiClient> AdminAsync() => LoginAsync(ApiFactory.AdminLogin, ApiFactory.AdminPassword);

    public async Task<ApiClient> RegisterAsync(string username)
    {
        var client = Anonymous();
        var session = await client.PostAsync("/api/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            displayName = "Độc giả " + username,
            password = "Mat-khau-doc-gia-123",
        });
        client.Token = session.Str("accessToken");
        client.UserId = session.GetProperty("user").Id();
        client.Username = username;
        return client;
    }

    /// <summary>SystemAdmin creates a house + new owner account; the owner logs in and changes the temporary password.</summary>
    public async Task<(ApiClient Owner, Guid HouseId)> CreateHouseAsync(ApiClient admin, string name, string ownerUsername)
    {
        var created = await admin.PostAsync("/api/admin/translation-houses", new
        {
            name,
            description = "Nhóm dịch dùng cho kiểm thử",
            owner = new { newAccount = new { username = ownerUsername, email = $"{ownerUsername}@example.com", displayName = "Chủ nhóm " + name } },
        }, HttpStatusCode.Created);

        var houseId = created.GetProperty("house").Id();
        var temp = created.Str("generatedPassword");
        var owner = await LoginAsync(ownerUsername, temp);
        var changed = await owner.PostAsync("/api/auth/change-password", new { currentPassword = temp, newPassword = "Mat-khau-chu-nhom-123" });
        owner.Token = changed.Str("accessToken");
        owner.HouseId = houseId;
        return (owner, houseId);
    }

    public async Task<Guid> CreateComicAsync(ApiClient owner, string title, params string[] tags)
    {
        var comic = await owner.PostAsync("/api/translation-house/comics", new
        {
            translatedTitle = title,
            originalTitle = title + " (original)",
            description = "Mô tả truyện " + title,
            author = "Tác giả",
            status = "ongoing",
            tags,
        }, HttpStatusCode.Created);
        return comic.Id();
    }

    public async Task<Guid> CreateChapterAsync(ApiClient owner, Guid comicId, decimal number, string? title = null)
    {
        var chapter = await owner.PostAsync($"/api/translation-house/comics/{comicId}/chapters", new { number, title }, HttpStatusCode.Created);
        return chapter.Id();
    }

    public async Task<JsonElement> UploadAndWaitAsync(ApiClient owner, Guid chapterId, byte[]? zip = null, string expectedStatus = "succeeded")
    {
        using var response = await owner.UploadAsync($"/api/translation-house/chapters/{chapterId}/upload", zip ?? SampleChapterZip(), "chuong.zip");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"upload: {(int)response.StatusCode} {text}");
        var job = await WaitForJobAsync(owner, chapterId);
        Assert.True(job.Str("status") == expectedStatus, $"job ended as {job.Str("status")}: {job}");
        return job;
    }

    public static async Task<JsonElement> WaitForJobAsync(ApiClient owner, Guid chapterId, int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var job = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}/processing");
            var status = job.Str("status");
            if (status is "succeeded" or "failed")
            {
                return job;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Job for chapter {chapterId} still {status}");
            }

            await Task.Delay(150);
        }
    }

    /// <summary>House + comic + chapter, uploaded, processed and published.</summary>
    public async Task<(Guid ComicId, Guid ChapterId)> PublishedChapterAsync(ApiClient owner, string title, decimal number = 1)
    {
        var comicId = await CreateComicAsync(owner, title, "Hành động");
        var chapterId = await CreateChapterAsync(owner, comicId, number, "Khởi đầu");
        await UploadAndWaitAsync(owner, chapterId);
        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
        return (comicId, chapterId);
    }

    // ---------------- test data ----------------
    static Scenario() => NetVipsImageProcessor.EnsureInitialized();

    public static byte[] Png(int w, int h, int shade) => Solid(w, h, shade).PngsaveBuffer();

    public static byte[] Jpeg(int w, int h, int shade) => Solid(w, h, shade).JpegsaveBuffer(q: 85);

    public static byte[] Webp(int w, int h, int shade) => Solid(w, h, shade).WebpsaveBuffer(q: 80);

    private static Image Solid(int w, int h, int shade)
    {
        using var black = Image.Black(w, h, bands: 3);
        return (black + new double[] { shade, 255 - shade, 128 }).Cast(Enums.BandFormat.Uchar);
    }

    /// <summary>Three pages in mixed formats and deliberately unsorted names (10 after 2).</summary>
    public static byte[] SampleChapterZip(int pages = 3) => Zip(Enumerable.Range(1, pages).Select(i => (i % 3) switch
    {
        1 => ($"trang{i}.png", Png(320, 480, i * 30)),
        2 => ($"trang{i}.jpg", Jpeg(320, 480, i * 30)),
        _ => ($"trang{i}.webp", Webp(320, 480, i * 30)),
    }).ToArray());

    public static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        }

        return ms.ToArray();
    }
}
