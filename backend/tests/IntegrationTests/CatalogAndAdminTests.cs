using System.Net;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ComicPlatform.IntegrationTests;

public class PublicCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Visibility_search_filters_and_navigation()
    {
        var admin = await _s.AdminAsync();
        var (owner, houseId) = await _s.CreateHouseAsync(admin, "Mèo Dịch Truyện", "chu_meo");
        var anon = _s.Anonymous();

        var draftComic = await _s.CreateComicAsync(owner, "Truyện Chưa Đăng");
        var (comicId, ch1) = await _s.PublishedChapterAsync(owner, "Thám Tử Lừng Danh");
        var ch2 = await _s.CreateChapterAsync(owner, comicId, 2, "Vụ án thứ hai");
        await _s.UploadAndWaitAsync(owner, ch2);
        await owner.PostAsync($"/api/translation-house/chapters/{ch2}/publish");

        var list = await anon.GetAsync("/api/comics?pageSize=48");
        Assert.Contains(list.Items(), c => c.Id() == comicId);
        Assert.DoesNotContain(list.Items(), c => c.Id() == draftComic);
        var card = list.Items().First(c => c.Id() == comicId);
        Assert.Equal(2, card.Int("chapterCount"));
        Assert.Equal(2, card.GetProperty("latestChapters").GetArrayLength());

        // Diacritic-insensitive search.
        var search = await anon.GetAsync("/api/comics?q=tham%20tu%20lung");
        Assert.Contains(search.Items(), c => c.Id() == comicId);
        var none = await anon.GetAsync("/api/comics?q=khong%20co%20truyen%20nay");
        Assert.Empty(none.Items());

        // Tag + house filters, house page, tags list.
        Assert.Contains((await anon.GetAsync("/api/comics?tag=hanh-dong")).Items(), c => c.Id() == comicId);
        Assert.Contains((await anon.GetAsync("/api/comics?house=meo-dich-truyen")).Items(), c => c.Id() == comicId);
        var house = await anon.GetAsync("/api/houses/meo-dich-truyen");
        Assert.Equal(1, house.Int("comicCount"));
        Assert.Contains((await anon.GetAsync("/api/tags")).Items(), t => t.Str("slug") == "hanh-dong");

        // Chapter navigation.
        var reader1 = await anon.GetAsync($"/api/chapters/{ch1}/reader");
        Assert.Equal(ch2, reader1.GetProperty("chapter").GetProperty("next").Id());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reader1.GetProperty("chapter").GetProperty("prev").ValueKind);
        var chapters = await anon.GetAsync($"/api/comics/{comicId}/chapters");
        Assert.Equal([2m, 1m], chapters.Items().Select(c => c.GetProperty("number").GetDecimal()));

        // Suspending the house hides everything; activating restores it.
        await admin.PostAsync($"/api/admin/translation-houses/{houseId}/suspend", new { reason = "Vi phạm bản quyền" });
        await anon.ProblemAsync(HttpMethod.Get, $"/api/comics/{comicId}", null, HttpStatusCode.NotFound);
        await anon.ProblemAsync(HttpMethod.Get, $"/api/chapters/{ch1}/reader", null, HttpStatusCode.NotFound);
        Assert.Equal("house_suspended", await owner.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));

        await admin.PostAsync($"/api/admin/translation-houses/{houseId}/activate");
        await anon.GetAsync($"/api/comics/{comicId}");
        await owner.GetAsync("/api/translation-house/comics");
    }

    [Fact]
    public async Task Az_sort_uses_vietnamese_collation()
    {
        var (owner, _) = await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm Sắp Xếp", "chu_sap_xep");
        foreach (var title in new[] { "Đường Về", "Dòng Sông", "Ăn Mày", "An Nhiên" })
        {
            await _s.PublishedChapterAsync(owner, title);
        }

        var list = await _s.Anonymous().GetAsync("/api/comics?house=nhom-sap-xep&sort=az");
        Assert.Equal(["An Nhiên", "Ăn Mày", "Dòng Sông", "Đường Về"], list.Items().Select(c => c.Str("translatedTitle")));
    }

    [Fact]
    public async Task Views_feed_rankings_and_library_features_work()
    {
        var (owner, _) = await _s.CreateHouseAsync(await _s.AdminAsync(), "Nhóm Xếp Hạng", "chu_xep_hang");
        var (comicId, chapterId) = await _s.PublishedChapterAsync(owner, "Truyện Hot Nhất");
        var reader = await _s.RegisterAsync("fan_cung");

        await reader.JsonAsync(HttpMethod.Post, $"/api/chapters/{chapterId}/view", new { }, HttpStatusCode.NoContent);
        await reader.JsonAsync(HttpMethod.Post, $"/api/chapters/{chapterId}/view", new { }, HttpStatusCode.NoContent); // deduplicated
        await factory.Services.GetRequiredService<ViewFlushService>().FlushAsync(CancellationToken.None);
        var ranking = await _s.Anonymous().GetAsync("/api/rankings?period=day");
        Assert.Contains(ranking.Items(), c => c.Id() == comicId);

        await reader.JsonAsync(HttpMethod.Put, $"/api/me/follows/{comicId}", new { }, HttpStatusCode.NoContent);
        Assert.Contains((await reader.GetAsync("/api/me/follows")).Items(), c => c.Id() == comicId);
        await reader.JsonAsync(HttpMethod.Put, "/api/me/progress", new { chapterId, page = 3 }, HttpStatusCode.NoContent);
        var state = await reader.GetAsync($"/api/me/comics/{comicId}/state");
        Assert.True(state.GetProperty("following").GetBoolean());
        Assert.Equal(3, state.Int("lastPage"));
        Assert.Single((await reader.GetAsync("/api/me/history")).Items());
        await reader.JsonAsync(HttpMethod.Delete, $"/api/me/follows/{comicId}", null, HttpStatusCode.NoContent);
        Assert.Empty((await reader.GetAsync("/api/me/follows")).Items());

        await _s.Anonymous().ProblemAsync(HttpMethod.Get, "/api/me/follows", null, HttpStatusCode.Unauthorized);
    }
}

public class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Only_system_admins_reach_admin_endpoints()
    {
        var reader = await _s.RegisterAsync("khong_phai_admin");
        Assert.Equal("admin_only", await reader.ProblemAsync(HttpMethod.Get, "/api/admin/stats", null, HttpStatusCode.Forbidden));
        await _s.Anonymous().ProblemAsync(HttpMethod.Get, "/api/admin/stats", null, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task New_owner_must_change_temporary_password_first()
    {
        var admin = await _s.AdminAsync();
        var created = await admin.PostAsync("/api/admin/translation-houses", new
        {
            name = "Nhóm Mật Khẩu Tạm",
            owner = new { newAccount = new { username = "chu_tam", email = "chu_tam@example.com", displayName = "Chủ Tạm" } },
        }, HttpStatusCode.Created);
        var owner = await _s.LoginAsync("chu_tam", created.Str("generatedPassword"));
        var me = await owner.GetAsync("/api/auth/me");
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("owner", me.GetProperty("memberships")[0].Str("role"));
        Assert.Equal("password_change_required", await owner.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task House_management_members_and_metadata()
    {
        var admin = await _s.AdminAsync();
        var existing = await _s.RegisterAsync("chu_co_san");
        var created = await admin.PostAsync("/api/admin/translation-houses", new
        {
            name = "Nhóm Có Sẵn",
            slug = "nhom-co-san",
            owner = new { existingLogin = "chu_co_san" },
        }, HttpStatusCode.Created);
        var houseId = created.GetProperty("house").Id();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, created.GetProperty("generatedPassword").ValueKind);

        // Duplicate names are rejected.
        Assert.Equal("validation_failed", await admin.ProblemAsync(HttpMethod.Post, "/api/admin/translation-houses",
            new { name = "Nhóm Có Sẵn", owner = new { existingLogin = "chu_co_san" } }, HttpStatusCode.BadRequest));

        existing.HouseId = houseId;
        var (comicId, _) = await _s.PublishedChapterAsync(existing, "Truyện Kiểm Tra Quản Trị");

        var comics = await admin.GetAsync($"/api/admin/translation-houses/{houseId}/comics");
        var comic = comics.Items().Single();
        Assert.Equal(comicId, comic.Id());
        Assert.Equal(1, comic.Int("publishedChapterCount"));
        Assert.True(comic.GetProperty("storageBytes").GetInt64() > 0);
        Assert.False(comic.TryGetProperty("pages", out _));

        var staffUser = await _s.RegisterAsync("nv_admin_them");
        var member = await admin.PostAsync($"/api/admin/translation-houses/{houseId}/members", new { login = "nv_admin_them", role = "staff" });
        await admin.PutAsync($"/api/admin/translation-houses/{houseId}/members/{member.Id()}", new { role = "owner" });
        Assert.Equal(2, (await admin.GetAsync($"/api/admin/translation-houses/{houseId}/members")).GetArrayLength());
        await admin.DeleteAsync($"/api/admin/translation-houses/{houseId}/members/{member.Id()}");
        _ = staffUser;

        var detail = await admin.GetAsync($"/api/admin/translation-houses/{houseId}");
        Assert.Equal(1, detail.Int("comicCount"));

        var audit = await admin.GetAsync("/api/admin/audit-logs?action=house.");
        Assert.Contains(audit.Items(), a => a.Str("action") == "house.created" && a.Str("targetId") == houseId.ToString());

        var stats = await admin.GetAsync("/api/admin/stats");
        Assert.True(stats.Int("publishedChapters") >= 1);
        Assert.Equal(30, stats.GetProperty("uploadsPerDay").GetArrayLength());

        var system = await admin.GetAsync("/api/admin/system");
        Assert.Equal(2, system.GetProperty("processing").Int("imageConcurrency"));
    }

    [Fact]
    public async Task Users_can_be_disabled_enabled_and_reset()
    {
        var admin = await _s.AdminAsync();
        var user = await _s.RegisterAsync("se_bi_khoa");

        await admin.JsonAsync(HttpMethod.Post, $"/api/admin/users/{user.UserId}/disable", new { }, HttpStatusCode.NoContent);
        Assert.Equal("account_disabled", await _s.Anonymous().ProblemAsync(HttpMethod.Post, "/api/auth/login",
            new { login = "se_bi_khoa", password = "Mat-khau-doc-gia-123" }, HttpStatusCode.Forbidden));
        // Existing refresh tokens were revoked too.
        Assert.Equal("invalid_refresh_token", await user.ProblemAsync(HttpMethod.Post, "/api/auth/refresh", new { }, HttpStatusCode.Unauthorized));

        await admin.JsonAsync(HttpMethod.Post, $"/api/admin/users/{user.UserId}/enable", new { }, HttpStatusCode.NoContent);
        var reset = await admin.PostAsync($"/api/admin/users/{user.UserId}/reset-password");
        var relogged = await _s.LoginAsync("se_bi_khoa", reset.Str("temporaryPassword"));
        Assert.True((await relogged.GetAsync("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean());

        Assert.Equal("cannot_disable_self", await admin.ProblemAsync(HttpMethod.Post, $"/api/admin/users/{admin.UserId}/disable", new { }, HttpStatusCode.Conflict));
        var users = await admin.GetAsync("/api/admin/users?q=se_bi_khoa");
        Assert.Single(users.Items());
    }
}

public class SecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Security_headers_are_present()
    {
        using var response = await _s.Anonymous().SendAsync(HttpMethod.Get, "/api/comics");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Cors_allows_only_configured_origins()
    {
        var client = _s.Anonymous();
        using var allowed = await client.SendAsync(HttpMethod.Options, "/api/auth/login", null, r =>
        {
            r.Headers.Add("Origin", ApiFactory.AllowedOrigin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            r.Headers.Add("Access-Control-Request-Headers", "content-type");
        });
        Assert.Equal(ApiFactory.AllowedOrigin, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var denied = await client.SendAsync(HttpMethod.Options, "/api/auth/login", null, r =>
        {
            r.Headers.Add("Origin", "https://evil.example");
            r.Headers.Add("Access-Control-Request-Method", "POST");
        });
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Health_endpoints_report_status()
    {
        var anon = _s.Anonymous();
        Assert.Equal("ok", (await anon.GetAsync("/api/health")).Str("status"));
        var ready = await anon.GetAsync("/api/health/ready");
        Assert.Equal("ok", ready.GetProperty("checks").Str("database"));
    }

    /// <remarks>
    /// TestServer does not enforce Kestrel's MaxRequestBodySize (1 MB → 413); that limit is verified against a
    /// real Kestrel instance by the load/smoke tool. Here we only assert the request is rejected cleanly.
    /// </remarks>
    [Fact]
    public async Task Oversized_json_bodies_are_rejected()
    {
        var huge = new string('a', 2 * 1024 * 1024);
        using var response = await _s.Anonymous().SendAsync(HttpMethod.Post, "/api/auth/login", new { login = huge, password = "x" });
        Assert.True((int)response.StatusCode is 400 or 413, $"got {(int)response.StatusCode}");
    }
}

public class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_attempts_are_rate_limited_per_ip()
    {
        var client = new Scenario(factory).Anonymous();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 25; i++)
        {
            using var r = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { login = "ai_do_" + i, password = "sai-mat-khau" });
            statuses.Add(r.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(20, statuses.Count(s => s == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Silent_sign_in_probes_do_not_use_the_credential_budget()
    {
        // Every page load calls /auth/refresh; many readers can share one public IP (carrier NAT).
        var client = new Scenario(factory).Anonymous();
        for (var i = 0; i < 40; i++)
        {
            using var r = await client.SendAsync(HttpMethod.Post, "/api/auth/refresh", new { });
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }
    }
}
