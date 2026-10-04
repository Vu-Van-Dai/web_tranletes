using System.Net;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ComicPlatform.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor, WebSettings web) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => UserId is not null;

    public string? IpAddress => accessor.HttpContext is { } ctx ? ClientIp.Resolve(ctx, web) : null;
}

public static class ClientIp
{
    /// <summary>
    /// Trusts CF-Connecting-IP only when TRUST_CLOUDFLARE_HEADERS=true (origin firewalled to Cloudflare ranges);
    /// otherwise uses the socket address so clients cannot spoof their IP.
    /// </summary>
    public static string Resolve(HttpContext context, WebSettings web)
    {
        if (web.TrustCloudflareHeaders
            && context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cf)
            && IPAddress.TryParse(cf.ToString(), out var parsed))
        {
            return parsed.ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
