using System.Threading.RateLimiting;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ComicPlatform.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Public = "public";
    public const string Upload = "upload";
    public const string Write = "write";
    public const string Session = "session";

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services, WebSettings web)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await ProblemExceptionHandler.WriteAsync(ctx.HttpContext, 429, "rate_limited", "Bạn thao tác quá nhanh, vui lòng thử lại sau ít phút.");
            };

            if (!web.RateLimitsEnabled)
            {
                options.AddPolicy(Auth, _ => RateLimitPartition.GetNoLimiter("off"));
                options.AddPolicy(Public, _ => RateLimitPartition.GetNoLimiter("off"));
                options.AddPolicy(Upload, _ => RateLimitPartition.GetNoLimiter("off"));
                options.AddPolicy(Write, _ => RateLimitPartition.GetNoLimiter("off"));
                options.AddPolicy(Session, _ => RateLimitPartition.GetNoLimiter("off"));
                return;
            }

            // Global safety net. Signed-in users get their own budget (keyed by account) so that people sharing
            // one public IP — mobile carrier NAT, schools, internet cafés — do not exhaust each other's quota.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetTokenBucketLimiter(UserKey(ctx, web), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 600,
                    TokensPerPeriod = 600,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Credential endpoints: slow down brute force per IP.
            options.AddPolicy(Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp.Resolve(ctx, web), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            // Refresh/logout run on every page load (silent sign-in). The token is 256 random bits, so there is nothing to
            // brute-force; a per-IP budget far above the credential limit keeps users behind a shared IP signed in.
            options.AddPolicy(Session, ctx => RateLimitPartition.GetTokenBucketLimiter(ClientIp.Resolve(ctx, web), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 300,
                TokensPerPeriod = 300,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            options.AddPolicy(Public, ctx => RateLimitPartition.GetTokenBucketLimiter(UserKey(ctx, web), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 480,
                TokensPerPeriod = 480,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            // Uploads: at most 2 concurrent per user.
            options.AddPolicy(Upload, ctx => RateLimitPartition.GetConcurrencyLimiter(UserKey(ctx, web), _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                QueueLimit = 0,
            }));

            options.AddPolicy(Write, ctx => RateLimitPartition.GetTokenBucketLimiter(UserKey(ctx, web), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 300,
                TokensPerPeriod = 300,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        });

        return services;
    }

    /// <summary>Account id for authenticated requests (signature already verified), otherwise the client IP.</summary>
    private static string UserKey(HttpContext ctx, WebSettings web) =>
        ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is { } sub ? "u:" + sub : "ip:" + ClientIp.Resolve(ctx, web);
}
