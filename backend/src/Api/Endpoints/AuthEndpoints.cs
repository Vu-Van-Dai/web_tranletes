using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Identity;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshCookie = "cp_rt";

    public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, MeResponse User);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/register", async (RegisterRequest request, AuthService service, HttpContext http, WebSettings web, CancellationToken ct) =>
            Session(http, web, await service.RegisterAsync(request, ct)))
            .RequireRateLimiting(RateLimitPolicies.Auth);

        auth.MapPost("/login", async (LoginRequest request, AuthService service, HttpContext http, WebSettings web, CancellationToken ct) =>
            Session(http, web, await service.LoginAsync(request, ct)))
            .RequireRateLimiting(RateLimitPolicies.Auth);

        auth.MapPost("/refresh", async (AuthService service, HttpContext http, WebSettings web, CancellationToken ct) =>
        {
            EnsureTrustedOrigin(http, web);
            var refreshToken = http.Request.Cookies[RefreshCookie];
            if (string.IsNullOrEmpty(refreshToken))
            {
                // No cookie = an anonymous visitor's silent sign-in probe, not an error (keeps browser consoles clean).
                return Results.NoContent();
            }

            try
            {
                return Session(http, web, await service.RefreshAsync(refreshToken, ct));
            }
            catch (AppException ex) when (ex.Status == 401)
            {
                ClearCookie(http, web);
                throw;
            }
        }).RequireRateLimiting(RateLimitPolicies.Session);

        auth.MapPost("/logout", async (AuthService service, HttpContext http, WebSettings web, CancellationToken ct) =>
        {
            EnsureTrustedOrigin(http, web);
            await service.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
            ClearCookie(http, web);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitPolicies.Session);

        auth.MapPost("/change-password", async (ChangePasswordRequest request, AuthService service, ICurrentUser user, HttpContext http, WebSettings web, CancellationToken ct) =>
            Session(http, web, await service.ChangePasswordAsync(user.UserId!.Value, request, ct)))
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Auth);

        auth.MapGet("/me", async (AuthService service, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await service.GetMeAsync(user.UserId!.Value, ct)))
            .RequireAuthorization();

        return api;
    }

    private static IResult Session(HttpContext http, WebSettings web, AuthResult result)
    {
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = web.SecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = result.RefreshExpiresAt,
            IsEssential = true,
        });

        return Results.Ok(new SessionResponse(result.AccessToken, result.ExpiresAt, result.User));
    }

    private static void ClearCookie(HttpContext http, WebSettings web) =>
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = web.SecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
        });

    /// <summary>
    /// Cookie-authenticated endpoints must come from an allowed origin (CSRF defense in depth on top of
    /// SameSite=Lax). Requests without an Origin header (non-browser clients) are allowed.
    /// </summary>
    private static void EnsureTrustedOrigin(HttpContext http, WebSettings web)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return;
        }

        var self = $"{http.Request.Scheme}://{http.Request.Host}";
        if (string.Equals(origin, self, StringComparison.OrdinalIgnoreCase)
            || web.CorsAllowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        throw Errors.Forbidden("origin_not_allowed", "Nguồn yêu cầu không được phép.");
    }
}
