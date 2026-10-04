using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Tenancy;

namespace ComicPlatform.Api.Infrastructure;

public static class EndpointAuthorization
{
    public const string HouseHeader = "X-House-Id";

    /// <summary>
    /// Authenticated + house context resolved from DB memberships (fail closed). Must be applied to the
    /// whole /api/translation-house group so no house endpoint can run without a tenant.
    /// </summary>
    public static RouteGroupBuilder RequireHouseContext(this RouteGroupBuilder group)
    {
        group.RequireAuthorization();
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var userId = http.RequestServices.GetRequiredService<ICurrentUser>().UserId ?? throw Errors.Unauthorized();
            var access = http.RequestServices.GetRequiredService<HouseAccessService>();
            await access.ResolveHouseContextAsync(userId, http.Request.Headers[HouseHeader].ToString(), http.RequestAborted);
            return await next(ctx);
        });
        return group;
    }

    /// <summary>Requires a house permission (owners hold all). Runs after the group's house-context filter.</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, HousePermissions permission) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.RequestServices.GetRequiredService<HouseAccessService>().EnsurePermission(permission);
            return await next(ctx);
        });

    /// <summary>SystemAdmin re-verified from the database on every request (not trusted from the JWT).</summary>
    public static RouteGroupBuilder RequireSystemAdmin(this RouteGroupBuilder group)
    {
        group.RequireAuthorization();
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var userId = http.RequestServices.GetRequiredService<ICurrentUser>().UserId ?? throw Errors.Unauthorized();
            await http.RequestServices.GetRequiredService<HouseAccessService>().EnsureSystemAdminAsync(userId, http.RequestAborted);
            return await next(ctx);
        });
        return group;
    }
}
