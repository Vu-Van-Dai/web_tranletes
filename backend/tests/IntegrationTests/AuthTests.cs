using System.Net;
using ComicPlatform.IntegrationTests.Infrastructure;

namespace ComicPlatform.IntegrationTests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Register_login_me_round_trip()
    {
        var reader = await _s.RegisterAsync("docgia_mot");
        var me = await reader.GetAsync("/api/auth/me");
        Assert.Equal("docgia_mot", me.Str("username"));
        Assert.Equal("user", me.Str("systemRole"));
        Assert.Empty(me.GetProperty("memberships").EnumerateArray());

        var again = await _s.LoginAsync("docgia_mot@example.com", "Mat-khau-doc-gia-123");
        Assert.Equal(reader.UserId, again.UserId);
    }

    [Fact]
    public async Task Register_validates_input_with_vietnamese_messages()
    {
        var client = _s.Anonymous();
        using var response = await client.SendAsync(HttpMethod.Post, "/api/auth/register", new { username = "AB", email = "khong-hop-le", displayName = "", password = "ngan" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("validation_failed", body, StringComparison.Ordinal);
        Assert.Contains("Email không hợp lệ", body, StringComparison.Ordinal);
        Assert.Contains("\"password\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_registration_is_rejected()
    {
        await _s.RegisterAsync("trung_ten");
        var code = await _s.Anonymous().ProblemAsync(HttpMethod.Post, "/api/auth/register",
            new { username = "trung_ten", email = "khac@example.com", displayName = "Khác", password = "Mat-khau-doc-gia-123" }, HttpStatusCode.BadRequest);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_lockout_kicks_in()
    {
        await _s.RegisterAsync("hay_quen");
        var anon = _s.Anonymous();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("invalid_credentials", await anon.ProblemAsync(HttpMethod.Post, "/api/auth/login", new { login = "hay_quen", password = "sai-mat-khau-" + i }, HttpStatusCode.Unauthorized));
        }

        // Even the right password is refused while locked.
        Assert.Equal("account_locked", await anon.ProblemAsync(HttpMethod.Post, "/api/auth/login", new { login = "hay_quen", password = "Mat-khau-doc-gia-123" }, HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Unknown_account_gets_the_same_error_as_wrong_password() =>
        Assert.Equal("invalid_credentials", await _s.Anonymous().ProblemAsync(HttpMethod.Post, "/api/auth/login",
            new { login = "khong_ton_tai", password = "whatever-password" }, HttpStatusCode.Unauthorized));

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_family()
    {
        var reader = await _s.RegisterAsync("xoay_vong");
        var first = reader.RefreshCookie;
        Assert.NotNull(first);

        var refreshed = await reader.PostAsync("/api/auth/refresh");
        Assert.False(string.IsNullOrEmpty(refreshed.Str("accessToken")));
        var second = reader.RefreshCookie;
        Assert.NotEqual(first, second);

        // Replay the old token → theft assumed → whole family revoked (second token dies too).
        var attacker = _s.Anonymous();
        attacker.RefreshCookie = first;
        Assert.Equal("invalid_refresh_token", await attacker.ProblemAsync(HttpMethod.Post, "/api/auth/refresh", new { }, HttpStatusCode.Unauthorized));
        Assert.Equal("invalid_refresh_token", await reader.ProblemAsync(HttpMethod.Post, "/api/auth/refresh", new { }, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_an_empty_204_not_an_error()
    {
        using var response = await _s.Anonymous().SendAsync(HttpMethod.Post, "/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var reader = await _s.RegisterAsync("dang_xuat");
        var cookie = reader.RefreshCookie;
        await reader.JsonAsync(HttpMethod.Post, "/api/auth/logout", new { }, HttpStatusCode.NoContent);
        var replay = _s.Anonymous();
        replay.RefreshCookie = cookie;
        await replay.ProblemAsync(HttpMethod.Post, "/api/auth/refresh", new { }, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_from_a_foreign_origin_is_refused()
    {
        var reader = await _s.RegisterAsync("nguon_la");
        using var response = await reader.SendAsync(HttpMethod.Post, "/api/auth/refresh", new { }, r => r.Headers.Add("Origin", "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        var reader = await _s.RegisterAsync("doi_mk");
        Assert.Equal("validation_failed", await reader.ProblemAsync(HttpMethod.Post, "/api/auth/change-password",
            new { currentPassword = "sai-hoan-toan", newPassword = "Mat-khau-moi-12345" }, HttpStatusCode.BadRequest));
        await reader.PostAsync("/api/auth/change-password", new { currentPassword = "Mat-khau-doc-gia-123", newPassword = "Mat-khau-moi-12345" });
        await _s.LoginAsync("doi_mk", "Mat-khau-moi-12345");
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        var anon = _s.Anonymous();
        Assert.Equal("unauthorized", await anon.ProblemAsync(HttpMethod.Get, "/api/auth/me", null, HttpStatusCode.Unauthorized));

        anon.Token = "not.a.jwt";
        await anon.ProblemAsync(HttpMethod.Get, "/api/auth/me", null, HttpStatusCode.Unauthorized);

        // alg=none token with a forged admin claim
        anon.Token = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0." +
                     Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode($"{{\"sub\":\"{Guid.NewGuid()}\",\"sr\":\"admin\",\"exp\":4102444800}}") + ".";
        await anon.ProblemAsync(HttpMethod.Get, "/api/admin/stats", null, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registration_can_be_disabled_by_the_admin()
    {
        var admin = await _s.AdminAsync();
        await admin.PutAsync("/api/admin/settings", new { registrationEnabled = false });
        try
        {
            Assert.Equal("registration_disabled", await _s.Anonymous().ProblemAsync(HttpMethod.Post, "/api/auth/register",
                new { username = "bi_chan", email = "bi_chan@example.com", displayName = "Bị chặn", password = "Mat-khau-doc-gia-123" }, HttpStatusCode.Forbidden));
        }
        finally
        {
            await admin.PutAsync("/api/admin/settings", new { registrationEnabled = true });
        }
    }
}
