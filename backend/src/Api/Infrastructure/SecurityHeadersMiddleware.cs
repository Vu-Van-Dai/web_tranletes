namespace ComicPlatform.Api.Infrastructure;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Cross-Origin-Resource-Policy"] = context.Request.Path.StartsWithSegments("/cdn") ? "cross-origin" : "same-site";
            h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (context.Request.Path.StartsWithSegments("/api") && !h.ContainsKey("Cache-Control"))
            {
                h.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
