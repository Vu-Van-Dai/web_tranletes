using System.Net;
using ComicPlatform.IntegrationTests.Infrastructure;

namespace ComicPlatform.IntegrationTests;

/// <summary>Shared state: two independent houses (ABC, XYZ) with published content.</summary>
public sealed class TwoHousesFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();
    public Scenario S { get; private set; } = null!;
    public ApiClient Admin { get; private set; } = null!;
    public ApiClient Abc { get; private set; } = null!;
    public ApiClient Xyz { get; private set; } = null!;
    public Guid AbcHouse { get; private set; }
    public Guid XyzHouse { get; private set; }
    public Guid AbcComic { get; private set; }
    public Guid AbcChapter { get; private set; }
    public Guid XyzComic { get; private set; }
    public Guid XyzChapter { get; private set; }
    public Guid XyzDraftChapter { get; private set; }
    public Guid XyzOwnerMemberId { get; private set; }

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)Factory).InitializeAsync();
        S = new Scenario(Factory);
        Admin = await S.AdminAsync();
        (Abc, AbcHouse) = await S.CreateHouseAsync(Admin, "Nhóm dịch ABC", "chu_abc");
        (Xyz, XyzHouse) = await S.CreateHouseAsync(Admin, "Nhóm dịch XYZ", "chu_xyz");
        (AbcComic, AbcChapter) = await S.PublishedChapterAsync(Abc, "Truyện của ABC");
        (XyzComic, XyzChapter) = await S.PublishedChapterAsync(Xyz, "Truyện của XYZ");
        XyzDraftChapter = await S.CreateChapterAsync(Xyz, XyzComic, 2);
        var members = await Xyz.GetAsync("/api/translation-house/members");
        XyzOwnerMemberId = members.Items().First().Id();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)Factory).DisposeAsync();
}

public class TenantIsolationTests(TwoHousesFixture f) : IClassFixture<TwoHousesFixture>
{
    public static TheoryData<string, string> ForeignResourceEndpoints() => new()
    {
        { "GET", "/api/translation-house/comics/{xyzComic}" },
        { "PUT", "/api/translation-house/comics/{xyzComic}" },
        { "DELETE", "/api/translation-house/comics/{xyzComic}" },
        { "GET", "/api/translation-house/comics/{xyzComic}/chapters" },
        { "POST", "/api/translation-house/comics/{xyzComic}/chapters" },
        { "GET", "/api/translation-house/chapters/{xyzChapter}" },
        { "PUT", "/api/translation-house/chapters/{xyzChapter}" },
        { "DELETE", "/api/translation-house/chapters/{xyzDraft}" },
        { "GET", "/api/translation-house/chapters/{xyzChapter}/processing" },
        { "POST", "/api/translation-house/chapters/{xyzChapter}/publish" },
        { "POST", "/api/translation-house/chapters/{xyzChapter}/unpublish" },
        { "POST", "/api/translation-house/chapters/{xyzChapter}/retry" },
        { "GET", "/api/translation-house/chapters/{xyzChapter}/versions/1/preview" },
        { "GET", "/api/translation-house/chapters/{xyzChapter}/versions/1/package" },
        { "PUT", "/api/translation-house/members/{xyzMember}" },
        { "DELETE", "/api/translation-house/members/{xyzMember}" },
    };

    private string Resolve(string template) => template
        .Replace("{xyzComic}", f.XyzComic.ToString(), StringComparison.Ordinal)
        .Replace("{xyzChapter}", f.XyzChapter.ToString(), StringComparison.Ordinal)
        .Replace("{xyzDraft}", f.XyzDraftChapter.ToString(), StringComparison.Ordinal)
        .Replace("{xyzMember}", f.XyzOwnerMemberId.ToString(), StringComparison.Ordinal);

    private static object Body(string url) => url.Contains("/chapters", StringComparison.Ordinal) && !url.Contains("/comics/", StringComparison.Ordinal)
        ? new { number = 99 }
        : url.Contains("/members/", StringComparison.Ordinal)
            ? new { role = "staff" }
            : new { translatedTitle = "Chiếm quyền", originalTitle = "Hijack", number = 99 };

    [Theory]
    [MemberData(nameof(ForeignResourceEndpoints))]
    public async Task Abc_cannot_touch_xyz_resources(string method, string template)
    {
        var url = Resolve(template);
        var code = await f.Abc.ProblemAsync(new HttpMethod(method), url, method is "GET" or "DELETE" ? null : Body(url), HttpStatusCode.Forbidden);
        Assert.Equal("cross_tenant", code);
    }

    [Fact]
    public async Task Abc_cannot_upload_into_xyz_chapter()
    {
        using var response = await f.Abc.UploadAsync($"/api/translation-house/chapters/{f.XyzDraftChapter}/upload", Scenario.SampleChapterZip(1), "x.zip");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_attempts_leave_xyz_data_untouched_and_are_audited()
    {
        await f.Abc.ProblemAsync(HttpMethod.Put, $"/api/translation-house/comics/{f.XyzComic}", new { translatedTitle = "Đã bị sửa", originalTitle = "x" }, HttpStatusCode.Forbidden);
        var comic = await f.Xyz.GetAsync($"/api/translation-house/comics/{f.XyzComic}");
        Assert.Equal("Truyện của XYZ", comic.Str("translatedTitle"));

        var audit = await f.Admin.GetAsync("/api/admin/audit-logs?action=security.cross_tenant_denied&pageSize=200");
        Assert.Contains(audit.Items(), a => a.Str("targetId") == f.XyzComic.ToString());
    }

    [Fact]
    public async Task Lists_only_show_own_house_data()
    {
        var comics = await f.Abc.GetAsync("/api/translation-house/comics");
        Assert.All(comics.Items(), c => Assert.NotEqual(f.XyzComic, c.Id()));
        Assert.Contains(comics.Items(), c => c.Id() == f.AbcComic);

        var members = await f.Abc.GetAsync("/api/translation-house/members");
        Assert.All(members.Items(), m => Assert.NotEqual("chu_xyz", m.Str("username")));

        var jobs = await f.Abc.GetAsync("/api/translation-house/processing");
        Assert.All(jobs.Items(), j => Assert.NotEqual(f.XyzChapter, j.Id("chapterId")));

        var dashboard = await f.Abc.GetAsync("/api/translation-house/dashboard");
        Assert.Equal(comics.Int("total"), dashboard.Int("comicCount"));
    }

    [Fact]
    public async Task Missing_ids_are_404_not_403()
    {
        Assert.Equal("not_found", await f.Abc.ProblemAsync(HttpMethod.Get, $"/api/translation-house/comics/{Guid.NewGuid()}", null, HttpStatusCode.NotFound));
        Assert.Equal("not_found", await f.Abc.ProblemAsync(HttpMethod.Get, $"/api/translation-house/chapters/{Guid.NewGuid()}", null, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Forged_house_selector_is_rejected()
    {
        var forged = new ApiClient(f.Factory.CreateClient()) { Token = f.Abc.Token, HouseId = f.XyzHouse };
        Assert.Equal("not_house_member", await forged.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task House_id_in_request_body_is_ignored()
    {
        var created = await f.Abc.PostAsync("/api/translation-house/comics", new
        {
            translationHouseId = f.XyzHouse,
            translatedTitle = "Truyện cài cắm",
            originalTitle = "Planted",
        }, HttpStatusCode.Created);

        // Visible to ABC, invisible to XYZ.
        await f.Abc.GetAsync($"/api/translation-house/comics/{created.Id()}");
        await f.Xyz.ProblemAsync(HttpMethod.Get, $"/api/translation-house/comics/{created.Id()}", null, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Readers_admins_and_anonymous_have_no_house_access()
    {
        var reader = await f.S.RegisterAsync("chi_doc_" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal("no_house_membership", await reader.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));
        Assert.Equal("no_house_membership", await f.Admin.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));
        await f.S.Anonymous().ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Unauthorized);
        Assert.Equal("admin_only", await f.Abc.ProblemAsync(HttpMethod.Get, "/api/admin/translation-houses", null, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Staff_permissions_are_enforced()
    {
        var staffUser = "bientap_" + Guid.NewGuid().ToString("N")[..6];
        var staff = await f.S.RegisterAsync(staffUser);
        await f.Abc.PostAsync("/api/translation-house/members", new { login = staffUser, role = "staff", permissions = new[] { "uploadChapters", "manageChapters" } });
        staff.HouseId = f.AbcHouse;

        // Allowed: create chapter. Denied: publish, edit comic, manage members, edit profile.
        await staff.PostAsync($"/api/translation-house/comics/{f.AbcComic}/chapters", new { number = 50 }, HttpStatusCode.Created);
        Assert.Equal("missing_permission", await staff.ProblemAsync(HttpMethod.Post, $"/api/translation-house/chapters/{f.AbcChapter}/unpublish", new { }, HttpStatusCode.Forbidden));
        Assert.Equal("missing_permission", await staff.ProblemAsync(HttpMethod.Put, $"/api/translation-house/comics/{f.AbcComic}", new { translatedTitle = "x", originalTitle = "y" }, HttpStatusCode.Forbidden));
        Assert.Equal("missing_permission", await staff.ProblemAsync(HttpMethod.Post, "/api/translation-house/members", new { login = "chu_xyz" }, HttpStatusCode.Forbidden));
        Assert.Equal("missing_permission", await staff.ProblemAsync(HttpMethod.Put, "/api/translation-house/profile", new { name = "Đổi tên" }, HttpStatusCode.Forbidden));
        Assert.Equal("missing_permission", await staff.ProblemAsync(HttpMethod.Get, "/api/translation-house/members", null, HttpStatusCode.Forbidden));

        // Staff of ABC still cannot reach XYZ.
        Assert.Equal("cross_tenant", await staff.ProblemAsync(HttpMethod.Get, $"/api/translation-house/comics/{f.XyzComic}", null, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Removed_members_lose_access_immediately()
    {
        var name = "tam_thoi_" + Guid.NewGuid().ToString("N")[..6];
        var temp = await f.S.RegisterAsync(name);
        var member = await f.Abc.PostAsync("/api/translation-house/members", new { login = name });
        temp.HouseId = f.AbcHouse;
        await temp.GetAsync("/api/translation-house/comics");

        await f.Abc.DeleteAsync($"/api/translation-house/members/{member.Id()}");
        Assert.Equal("not_house_member", await temp.ProblemAsync(HttpMethod.Get, "/api/translation-house/comics", null, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Last_owner_cannot_be_removed_or_demoted()
    {
        var members = await f.Abc.GetAsync("/api/translation-house/members");
        var ownerId = members.Items().First(m => m.Str("role") == "owner").Id();
        Assert.Equal("last_owner", await f.Abc.ProblemAsync(HttpMethod.Delete, $"/api/translation-house/members/{ownerId}", null, HttpStatusCode.Conflict));
        Assert.Equal("last_owner", await f.Abc.ProblemAsync(HttpMethod.Put, $"/api/translation-house/members/{ownerId}", new { role = "staff" }, HttpStatusCode.Conflict));
    }
}
