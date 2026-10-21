using System.Net;
using System.Text.Json;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ComicPlatform.IntegrationTests;

public class ViewsAndRevenueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    private static async Task<string> TicketAsync(ApiClient client, Guid chapterId) =>
        (await client.PostAsync($"/api/chapters/{chapterId}/view-ticket")).Str("ticket");

    private static Task ReportAsync(ApiClient client, Guid chapterId, string? ticket, int pages = 3) =>
        client.JsonAsync(HttpMethod.Post, $"/api/chapters/{chapterId}/view", new { ticket, pagesSeen = pages, activeSeconds = 5 }, HttpStatusCode.NoContent);

    private Task FlushAsync() => factory.Services.GetRequiredService<ViewFlushService>().FlushAsync(CancellationToken.None);

    [Fact]
    public async Task Only_verified_views_are_counted_and_paid_per_chapter()
    {
        var admin = await _s.AdminAsync();
        await admin.PutAsync("/api/admin/settings", new { revenuePerThousandViews = 20000 });
        var (owner, houseId) = await _s.CreateHouseAsync(admin, "Nhóm Lượt Xem", "chu_luot_xem");
        var (comicId, ch1) = await _s.PublishedChapterAsync(owner, "Truyện Có Doanh Thu");
        var ch2 = await _s.CreateChapterAsync(owner, comicId, 2);
        await _s.UploadAndWaitAsync(owner, ch2);
        await owner.PostAsync($"/api/translation-house/chapters/{ch2}/publish");

        var fan = await _s.RegisterAsync("doc_gia_that");
        var guest = _s.Anonymous();
        guest.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148";

        var fanTicket = await TicketAsync(fan, ch1);
        var guestTicket = await TicketAsync(guest, ch1);
        var fanTicket2 = await TicketAsync(fan, ch2);
        var tooFast = await TicketAsync(fan, ch2);
        await ReportAsync(fan, ch2, tooFast); // sent before the required reading time
        await Task.Delay(1300);

        await ReportAsync(fan, ch1, fanTicket);                          // counted
        await ReportAsync(fan, ch1, fanTicket);                          // replay
        await ReportAsync(fan, ch1, await TicketAsync(fan, ch1));        // too fast (new ticket)
        await ReportAsync(guest, ch1, guestTicket);                      // counted (another visitor)
        await ReportAsync(fan, ch2, fanTicket2);                         // counted
        await ReportAsync(fan, ch2, fanTicket2[..^4] + "AAAA");         // forged
        await ReportAsync(fan, ch1, null);                               // no ticket (old client / script)

        var stranger = _s.Anonymous();
        await ReportAsync(stranger, ch1, guestTicket);                   // ticket handed to another machine

        var bot = _s.Anonymous();
        bot.UserAgent = "python-requests/2.32";
        await ReportAsync(bot, ch1, guestTicket);

        var ownTicket = await TicketAsync(owner, ch1);
        await Task.Delay(1300);
        await ReportAsync(owner, ch1, ownTicket);                        // house members do not earn views

        var again = await TicketAsync(fan, ch1);
        await Task.Delay(1300);
        await ReportAsync(fan, ch1, again);                              // same reader, same chapter, same day
        var incomplete = await TicketAsync(guest, ch2);
        await Task.Delay(1300);
        await ReportAsync(guest, ch2, incomplete, pages: 0);             // left without reading

        await FlushAsync();

        var report = await owner.GetAsync("/api/translation-house/stats/views");
        Assert.True(report.GetProperty("revenueVisible").GetBoolean());
        var totals = report.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("views").GetInt64());
        Assert.Equal(60m, totals.GetProperty("revenue").GetDecimal());
        Assert.True(totals.GetProperty("suspiciousViews").GetInt64() >= 6);

        var comic = Assert.Single(report.GetProperty("comics").EnumerateArray());
        Assert.Equal(comicId, comic.Id("comicId"));
        var chapters = comic.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(comic.GetProperty("views").GetInt64(), chapters.Sum(c => c.GetProperty("views").GetInt64()));
        Assert.Equal(2, chapters.Single(c => c.Id("chapterId") == ch1).GetProperty("views").GetInt64());
        Assert.Equal(1, chapters.Single(c => c.Id("chapterId") == ch2).GetProperty("views").GetInt64());
        Assert.Equal(40m, chapters.Single(c => c.Id("chapterId") == ch1).GetProperty("revenue").GetDecimal());

        // Platform payout report and fraud signals.
        var platform = await admin.GetAsync("/api/admin/revenue");
        var row = platform.GetProperty("houses").EnumerateArray().Single(h => h.Id("houseId") == houseId);
        Assert.Equal(60m, row.GetProperty("revenue").GetDecimal());
        var reasons = platform.GetProperty("rejections").EnumerateArray().Select(r => r.Str("reason")).ToHashSet();
        Assert.Superset(new HashSet<string> { "too_fast", "replay", "invalid_ticket", "no_ticket", "visitor_mismatch", "bot", "own_house", "incomplete" }, reasons);
        var detail = await admin.GetAsync($"/api/admin/revenue/houses/{houseId}");
        Assert.Equal(3, detail.GetProperty("totals").GetProperty("views").GetInt64());

        // Rankings are fed by verified views only.
        Assert.Contains((await _s.Anonymous().GetAsync("/api/rankings?period=day")).Items(), c => c.Id() == comicId);

        // Reports are house-scoped; readers cannot see them.
        var (otherOwner, _) = await _s.CreateHouseAsync(admin, "Nhóm Khác Xem", "chu_khac_xem");
        Assert.Equal(0, (await otherOwner.GetAsync("/api/translation-house/stats/views")).GetProperty("totals").GetProperty("views").GetInt64());
        await fan.ProblemAsync(HttpMethod.Get, "/api/admin/revenue", null, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_see_views_but_not_revenue()
    {
        var admin = await _s.AdminAsync();
        var (owner, _) = await _s.CreateHouseAsync(admin, "Nhóm Có Nhân Viên", "chu_co_nv");
        await _s.RegisterAsync("nhan_vien_xem");
        await owner.PostAsync("/api/translation-house/members", new { login = "nhan_vien_xem", role = "staff", permissions = new[] { "uploadChapters" } });
        var staff = await _s.LoginAsync("nhan_vien_xem", "Mat-khau-doc-gia-123");
        var report = await staff.GetAsync("/api/translation-house/stats/views");
        Assert.False(report.GetProperty("revenueVisible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("totals").GetProperty("revenue").ValueKind);
    }
}

public class PromoPagesAndHouseProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Promotional_images_become_webp_pages_at_the_start_and_end()
    {
        var (owner, _) = await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm Poster", "chu_poster");
        var comicId = await _s.CreateComicAsync(owner, "Truyện Có Poster");
        var chapterId = await _s.CreateChapterAsync(owner, comicId, 1);

        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Scenario.Png(50, 70, 10)), "promoStart", "poster-dau.png" },
            { new ByteArrayContent(Scenario.Jpeg(60, 80, 20)), "promoEnd", "cam-on.jpg" },
            { new ByteArrayContent(Scenario.SampleChapterZip(3)), "file", "chuong-1.zip" },
        };
        using (var response = await owner.SendAsync(HttpMethod.Post, $"/api/translation-house/chapters/{chapterId}/upload", form))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await Scenario.WaitForJobAsync(owner, chapterId);
        var preview = await owner.GetAsync($"/api/translation-house/chapters/{chapterId}/versions/1/preview");
        var pages = preview.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal(5, pages.Count);
        Assert.StartsWith("[Ảnh quảng bá đầu chương] poster-dau.png", pages[0].Str("sourceName"));
        Assert.StartsWith("[Ảnh quảng bá cuối chương] cam-on.jpg", pages[^1].Str("sourceName"));
        Assert.Equal(50, pages[0].Int("w"));

        // At most two promotional images; a non-image is rejected before the archive is read.
        var tooMany = new MultipartFormDataContent
        {
            { new ByteArrayContent(Scenario.Png(10, 10, 1)), "promoStart", "a.png" },
            { new ByteArrayContent(Scenario.Png(10, 10, 1)), "promoStart", "b.png" },
            { new ByteArrayContent(Scenario.Png(10, 10, 1)), "promoEnd", "c.png" },
            { new ByteArrayContent(Scenario.SampleChapterZip(1)), "file", "x.zip" },
        };
        Assert.Equal("too_many_extra_pages", await owner.ProblemAsync(HttpMethod.Post, $"/api/translation-house/chapters/{chapterId}/upload", tooMany, HttpStatusCode.BadRequest));
        var notImage = new MultipartFormDataContent
        {
            { new ByteArrayContent("<svg/>"u8.ToArray()), "promoStart", "a.svg" },
            { new ByteArrayContent(Scenario.SampleChapterZip(1)), "file", "x.zip" },
        };
        Assert.Equal("unsupported_image", await owner.ProblemAsync(HttpMethod.Post, $"/api/translation-house/chapters/{chapterId}/upload", notImage, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task House_profile_details_and_cover_are_public()
    {
        var (owner, _) = await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm Hồ Sơ Đầy Đủ", "chu_ho_so");
        await owner.PutAsync("/api/translation-house/profile", new
        {
            name = "Nhóm Hồ Sơ Đầy Đủ",
            slug = "nhom-ho-so",
            description = "Chuyên dịch truyện đời thường.",
            links = new { facebook = "https://facebook.com/nhom", tiktok = "https://tiktok.com/@nhom", donate = "https://ko-fi.com/nhom" },
            tagline = "Dịch bằng cả trái tim",
            contactEmail = "LienHe@Nhom.vn",
            foundedYear = 2021,
            recruiting = true,
            recruitmentInfo = "Cần tuyển editor.",
        });
        Assert.Equal("contactEmail", (await owner.JsonAsync(HttpMethod.Put, "/api/translation-house/profile",
            new { name = "Nhóm Hồ Sơ Đầy Đủ", contactEmail = "khong-hop-le" }, HttpStatusCode.BadRequest)).GetProperty("errors").EnumerateObject().First().Name);

        var form = new MultipartFormDataContent { { new ByteArrayContent(Scenario.Png(2000, 800, 40)), "file", "bia.png" } };
        using (var response = await owner.SendAsync(HttpMethod.Put, "/api/translation-house/profile/cover", form))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var house = await _s.Anonymous().GetAsync("/api/houses/nhom-ho-so");
        Assert.Matches(@"^/cdn/houses/[0-9a-f-]{36}/cover/[0-9a-f]{16}\.webp$", house.Str("coverUrl"));
        Assert.Equal("Dịch bằng cả trái tim", house.Str("tagline"));
        Assert.Equal("lienhe@nhom.vn", house.Str("contactEmail"));
        Assert.Equal(2021, house.Int("foundedYear"));
        Assert.True(house.GetProperty("recruiting").GetBoolean());
        Assert.Equal("https://tiktok.com/@nhom", house.GetProperty("links").Str("tiktok"));

        var removed = await owner.JsonAsync(HttpMethod.Delete, "/api/translation-house/profile/cover");
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("coverUrl").ValueKind);
    }

    [Fact]
    public async Task Ads_are_managed_by_admins_and_served_to_readers()
    {
        var admin = await _s.AdminAsync();
        var config = new
        {
            enabled = true,
            units = new object[]
            {
                new { id = "banner-1", name = "Banner đầu trang", enabled = true, format = "banner", imageUrl = "https://ads.example/b.webp", targetUrl = "https://shop.example", slots = new[] { "home_top", "reader_bottom" } },
                new { id = "popup-1", name = "Pop-up bắt buộc xem", enabled = true, format = "popup", html = "<a href='https://x.example'>Ad</a>", closeMode = "countdown", countdownSeconds = 5, pages = new[] { "reader" }, frequencyMinutes = 30 },
                new { id = "tab-1", name = "Tab mới", enabled = false, format = "newtab", targetUrl = "https://x.example" },
            },
        };
        await admin.PutAsync("/api/admin/ads", config);
        var site = await _s.Anonymous().GetAsync("/api/site");
        var units = site.GetProperty("ads").GetProperty("units").EnumerateArray().ToList();
        Assert.Equal(["banner-1", "popup-1"], units.Select(u => u.Str("id")));

        var invalid = new { enabled = true, units = new object[] { new { id = "x", name = "Sai", enabled = true, format = "popup", imageUrl = "https://a.example/a.webp", targetUrl = "https://a.example", closeMode = "countdown", countdownSeconds = 500 } } };
        await admin.JsonAsync(HttpMethod.Put, "/api/admin/ads", invalid, HttpStatusCode.BadRequest);

        var form = new MultipartFormDataContent { { new ByteArrayContent(Scenario.Png(900, 300, 90)), "file", "banner.png" } };
        using var uploaded = await admin.SendAsync(HttpMethod.Post, "/api/admin/ads/images", form);
        var url = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync()).RootElement.Str("url");
        Assert.Matches(@"^/cdn/ads/[0-9a-f]{16}\.webp$", url);

        var reader = await _s.RegisterAsync("khong_sua_qc");
        await reader.ProblemAsync(HttpMethod.Put, "/api/admin/ads", config, HttpStatusCode.Forbidden);
    }
}
