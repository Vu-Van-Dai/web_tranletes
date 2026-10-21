using System.Net;
using System.Text.Json;
using ComicPlatform.Infrastructure.Imaging;
using ComicPlatform.Infrastructure.Packaging;
using ComicPlatform.IntegrationTests.Infrastructure;

namespace ComicPlatform.IntegrationTests;

public class PublishingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    private async Task<ApiClient> OwnerAsync() =>
        (await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm " + Guid.NewGuid().ToString("N")[..6], "chu_" + Guid.NewGuid().ToString("N")[..8])).Owner;

    internal static async Task<(byte[] Bytes, HttpResponseMessage Response)> DownloadAsync(ApiClient client, string url)
    {
        var response = await client.SendAsync(HttpMethod.Get, url);
        return (await response.Content.ReadAsByteArrayAsync(), response);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Upload_process_publish_read_and_verify_package()
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Kiếm Khách Vô Danh", "Kiếm hiệp");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1, "Mở đầu");
        var anon = _s.Anonymous();

        var job = await _s.UploadAndWaitAsync(owner, chapterId, Scenario.SampleChapterZip(4));
        Assert.Equal(4, job.Int("progressTotal"));

        var detail = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}");
        Assert.Equal("ready", detail.Str("status"));
        Assert.Equal(1, detail.Int("readyVersion"));
        Assert.True(detail.GetProperty("actions").GetProperty("canPublish").GetBoolean());

        // Not public before publishing.
        await anon.ProblemAsync(HttpMethod.Get, $"/api/chapters/{chapterId}/reader", null, HttpStatusCode.NotFound);
        await anon.ProblemAsync(HttpMethod.Get, $"/api/comics/{comicId}", null, HttpStatusCode.NotFound);

        var published = await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
        Assert.Equal("published", published.Str("status"));
        Assert.Equal(1, published.Int("publishedVersion"));

        var reader = await anon.GetAsync($"/api/chapters/{chapterId}/reader");
        var package = reader.GetProperty("package");
        var url = package.Str("url");
        Assert.Matches(@"^/cdn/comics/[0-9a-f-]{36}/chapters/[0-9a-f-]{36}/v1\.pkg$", url);
        var pages = reader.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal(4, pages.Count);

        var (bytes, response) = await DownloadAsync(anon, url);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
            Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        Assert.Equal(package.GetProperty("size").GetInt64(), bytes.Length);
        using var ms = new MemoryStream(bytes);
        var info = await PackageReader.ReadAsync(ms, 200, verifyChecksum: true, CancellationToken.None);
        Assert.Equal(package.Str("sha256"), info.TrailerSha256Hex);
        for (var i = 0; i < info.Entries.Count; i++)
        {
            var entry = info.Entries[i];
            Assert.Equal(pages[i].Int("w"), entry.Width);
            Assert.Equal(pages[i].Int("h"), entry.Height);
            var pageBytes = bytes.AsSpan((int)(info.DataStart + entry.Offset), entry.Size);
            Assert.Equal(ImageFormat.Webp, ImageSignature.Detect(pageBytes));
        }

        // Public catalog now lists the comic with the chapter.
        var comic = await anon.GetAsync($"/api/comics/{comicId}");
        Assert.Equal(1, comic.Int("chapterCount"));
        Assert.Equal(chapterId, comic.GetProperty("firstChapter").Id());
    }

    [Fact]
    public async Task Preview_shows_the_processed_pages_in_archive_order_before_publishing()
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Truyện Xem Trước");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);

        // Numbering starts at 002, mixes zero padding and has junk files: natural order must win.
        var zip = Scenario.Zip(
            ("chuong/010.png", Scenario.Png(40, 50, 3)),
            ("chuong/002.png", Scenario.Png(20, 30, 1)),
            ("ghi-chu.txt", "Nhóm dịch Mây"u8.ToArray()),
            ("__MACOSX/chuong/._002.png", [1, 2, 3]),
            ("chuong/3.png", Scenario.Png(30, 40, 2)));
        await _s.UploadAndWaitAsync(owner, chapterId, zip);

        var preview = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}/versions/1/preview");
        Assert.Equal("ready", preview.Str("status"));
        Assert.True(preview.GetProperty("canPublish").GetBoolean());
        Assert.False(preview.GetProperty("isPublished").GetBoolean());
        var pages = preview.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal(["chuong/002.png", "chuong/3.png", "chuong/010.png"], pages.Select(p => p.Str("sourceName")));
        Assert.Equal([1, 2, 3], pages.Select(p => p.Int("n")));
        Assert.Equal([20, 30, 40], pages.Select(p => p.Int("w")));

        // The private package is the exact file that publishing will upload.
        var (bytes, response) = await DownloadAsync(owner, $"/api/translation-house/chapters/{chapterId}/versions/1/package");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(preview.GetProperty("fileSize").GetInt64(), bytes.Length);
        Assert.Equal(preview.Str("sha256"), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32))));

        await _s.Anonymous().ProblemAsync(HttpMethod.Get, $"/api/translation-house/chapters/{chapterId}/versions/1/preview", null, HttpStatusCode.Unauthorized);
        Assert.Equal("not_found", await owner.ProblemAsync(HttpMethod.Get, $"/api/translation-house/chapters/{chapterId}/versions/9/preview", null, HttpStatusCode.NotFound));

        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
        var after = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}/versions/1/preview");
        Assert.True(after.GetProperty("isPublished").GetBoolean());
        Assert.False(after.GetProperty("canPublish").GetBoolean());
    }

    [Fact]
    public async Task Unpublish_withdraws_and_republish_restores_the_object()
    {
        var owner = await OwnerAsync();
        var (comicId, chapterId) = await _s.PublishedChapterAsync(owner, "Rút Lại Rồi Đăng");
        var anon = _s.Anonymous();
        var url = (await anon.GetAsync($"/api/chapters/{chapterId}/reader")).GetProperty("package").Str("url");

        var after = await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/unpublish");
        Assert.Equal("unpublished", after.Str("status"));
        await anon.ProblemAsync(HttpMethod.Get, $"/api/chapters/{chapterId}/reader", null, HttpStatusCode.NotFound);
        await anon.ProblemAsync(HttpMethod.Get, $"/api/comics/{comicId}", null, HttpStatusCode.NotFound);

        // The outbox removes the object from the store.
        await WaitUntilAsync(async () =>
        {
            using var r = await anon.SendAsync(HttpMethod.Get, url);
            return r.StatusCode == HttpStatusCode.NotFound;
        });

        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
        using var restored = await anon.SendAsync(HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task New_version_replaces_the_published_one_only_when_published()
    {
        var owner = await OwnerAsync();
        var (_, chapterId) = await _s.PublishedChapterAsync(owner, "Sửa Lỗi Chính Tả");
        var anon = _s.Anonymous();
        var v1Url = (await anon.GetAsync($"/api/chapters/{chapterId}/reader")).GetProperty("package").Str("url");

        await _s.UploadAndWaitAsync(owner, chapterId, Scenario.SampleChapterZip(5));
        var detail = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}");
        Assert.Equal("published", detail.Str("status"));
        Assert.Equal(1, detail.Int("publishedVersion"));
        Assert.Equal(2, detail.Int("readyVersion"));

        // Readers still get v1 until the new version is published.
        Assert.Equal(v1Url, (await anon.GetAsync($"/api/chapters/{chapterId}/reader")).GetProperty("package").Str("url"));

        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
        var reader = await anon.GetAsync($"/api/chapters/{chapterId}/reader");
        Assert.EndsWith("/v2.pkg", reader.GetProperty("package").Str("url"), StringComparison.Ordinal);
        Assert.Equal(5, reader.GetProperty("pages").GetArrayLength());

        await WaitUntilAsync(async () =>
        {
            using var r = await anon.SendAsync(HttpMethod.Get, v1Url);
            return r.StatusCode == HttpStatusCode.NotFound;
        });
    }

    [Fact]
    public async Task Non_zip_uploads_are_rejected_immediately()
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Tệp Sai");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);
        using var response = await owner.UploadAsync($"/api/translation-house/chapters/{chapterId}/upload", "MZ this is an exe"u8.ToArray(), "chuong.zip");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_archive", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var notMultipart = await owner.SendAsync(HttpMethod.Post, $"/api/translation-house/chapters/{chapterId}/upload", new { file = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, notMultipart.StatusCode);
    }

    public static TheoryData<string, string> BadArchives() => new()
    {
        { "disguised_exe", "unsupported_image" },
        { "zip_slip", "unsafe_path" },
        { "no_images", "archive_empty" },
        { "corrupt_png", "corrupt_image" },
        { "bomb", "suspicious_compression" },
    };

    private static byte[] BadArchive(string kind) => kind switch
    {
        "disguised_exe" => Scenario.Zip(("trang1.jpg", "MZ\u0090\0 not an image at all, an executable"u8.ToArray())),
        "zip_slip" => Scenario.Zip(("../../../../evil.png", Scenario.Png(10, 10, 1))),
        "no_images" => Scenario.Zip(("ghi-chu.txt", "Chỉ có chữ"u8.ToArray())),
        "corrupt_png" => Scenario.Zip(("trang1.png", Scenario.Png(200, 200, 5)[..60])),
        "bomb" => Scenario.Zip(("bom.png", new byte[20 * 1024 * 1024])),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(BadArchives))]
    public async Task Bad_archives_fail_with_clear_codes(string kind, string expectedCode)
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Tệp Hỏng " + kind);
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);
        var job = await _s.UploadAndWaitAsync(owner, chapterId, BadArchive(kind), expectedStatus: "failed");
        Assert.Equal(expectedCode, job.Str("errorCode"));
        Assert.False(string.IsNullOrWhiteSpace(job.Str("errorMessage")));

        var detail = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}");
        Assert.Equal("failed", detail.Str("status"));
        Assert.True(detail.GetProperty("actions").GetProperty("canRetry").GetBoolean());
        Assert.False(detail.GetProperty("actions").GetProperty("canPublish").GetBoolean());
    }

    [Fact]
    public async Task Retry_requeues_a_failed_job()
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Thử Lại");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);
        await _s.UploadAndWaitAsync(owner, chapterId, BadArchive("disguised_exe"), expectedStatus: "failed");

        var retried = await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/retry");
        Assert.Equal("queued", retried.Str("status"));
        var job = await Scenario.WaitForJobAsync(owner, chapterId);
        Assert.Equal("failed", job.Str("status"));
        Assert.Equal(2, job.Int("attempts"));
    }

    [Fact]
    public async Task Published_chapters_cannot_be_deleted_until_unpublished()
    {
        var owner = await OwnerAsync();
        var (_, chapterId) = await _s.PublishedChapterAsync(owner, "Không Được Xoá");
        Assert.Equal("chapter_published", await owner.ProblemAsync(HttpMethod.Delete, $"/api/translation-house/chapters/{chapterId}", null, HttpStatusCode.Conflict));
        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/unpublish");
        await owner.DeleteAsync($"/api/translation-house/chapters/{chapterId}");
        await owner.ProblemAsync(HttpMethod.Get, $"/api/translation-house/chapters/{chapterId}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_comic_takes_it_offline()
    {
        var owner = await OwnerAsync();
        var (comicId, chapterId) = await _s.PublishedChapterAsync(owner, "Sẽ Bị Xoá");
        var anon = _s.Anonymous();
        var url = (await anon.GetAsync($"/api/chapters/{chapterId}/reader")).GetProperty("package").Str("url");

        await owner.DeleteAsync($"/api/translation-house/comics/{comicId}");
        await anon.ProblemAsync(HttpMethod.Get, $"/api/comics/{comicId}", null, HttpStatusCode.NotFound);
        await anon.ProblemAsync(HttpMethod.Get, $"/api/chapters/{chapterId}/reader", null, HttpStatusCode.NotFound);
        await owner.ProblemAsync(HttpMethod.Get, $"/api/translation-house/comics/{comicId}", null, HttpStatusCode.NotFound);
        await WaitUntilAsync(async () =>
        {
            using var r = await anon.SendAsync(HttpMethod.Get, url);
            return r.StatusCode == HttpStatusCode.NotFound;
        });
    }

    [Fact]
    public async Task Covers_are_processed_to_webp_and_served()
    {
        var owner = await OwnerAsync();
        var comicId = await _s.CreateComicAsync(owner, "Có Ảnh Bìa");
        var form = new MultipartFormDataContent { { new ByteArrayContent(Scenario.Png(1200, 1800, 70)), "file", "bia.png" } };
        using var response = await owner.SendAsync(HttpMethod.Put, $"/api/translation-house/comics/{comicId}/cover", form);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var coverUrl = body.Str("coverUrl");
        Assert.Matches(@"^/cdn/comics/[0-9a-f-]{36}/cover/[0-9a-f]{16}\.webp$", coverUrl);

        var (bytes, served) = await DownloadAsync(_s.Anonymous(), coverUrl);
        using (served)
        {
            Assert.Equal("image/webp", served.Content.Headers.ContentType!.MediaType);
        }

        Assert.Equal(ImageFormat.Webp, ImageSignature.Detect(bytes));

        var bad = new MultipartFormDataContent { { new ByteArrayContent("<svg/>"u8.ToArray()), "file", "bia.svg" } };
        using var rejected = await owner.SendAsync(HttpMethod.Put, $"/api/translation-house/comics/{comicId}/cover", bad);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task Origin_fallback_requires_the_secret_and_serves_only_public_objects()
    {
        var owner = await OwnerAsync();
        var (_, chapterId) = await _s.PublishedChapterAsync(owner, "Dự Phòng Gốc");
        var anon = _s.Anonymous();
        var key = (await anon.GetAsync($"/api/chapters/{chapterId}/reader")).GetProperty("package").Str("url")["/cdn/".Length..];

        using (var noSecret = await anon.SendAsync(HttpMethod.Get, "/internal/objects/" + key))
        {
            Assert.Equal(HttpStatusCode.NotFound, noSecret.StatusCode);
        }

        using (var ok = await anon.SendAsync(HttpMethod.Get, "/internal/objects/" + key, configure: r => r.Headers.Add("X-Origin-Secret", "integration-origin-secret-1234567890")))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", ok.Headers.CacheControl!.ToString());
        }

        await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/unpublish");
        using var hidden = await anon.SendAsync(HttpMethod.Get, "/internal/objects/" + key, configure: r => r.Headers.Add("X-Origin-Secret", "integration-origin-secret-1234567890"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using var traversal = await anon.SendAsync(HttpMethod.Get, "/cdn/comics/..%2F..%2Fappsettings.json");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
    }
}

public class UploadLimitTests(SmallLimitsApiFactory factory) : IClassFixture<SmallLimitsApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Oversized_uploads_are_rejected_with_413()
    {
        var (owner, _) = await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm giới hạn", "chu_gioi_han");
        var comicId = await _s.CreateComicAsync(owner, "Quá Lớn");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);
        var big = new byte[400 * 1024];
        Random.Shared.NextBytes(big);
        using var response = await owner.UploadAsync($"/api/translation-house/chapters/{chapterId}/upload", Scenario.Zip(("trang1.png", big)), "lon.zip");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var tooMany = Scenario.SampleChapterZip(4);
        var job = await _s.UploadAndWaitAsync(owner, chapterId, tooMany, expectedStatus: "failed");
        Assert.Equal("too_many_pages", job.Str("errorCode"));
    }
}
