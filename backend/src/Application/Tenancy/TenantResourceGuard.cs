using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Tenancy;

/// <summary>
/// Resource-based authorization for house-scoped ids taken from the URL.
/// Probes the owner without the tenant filter so the response can distinguish
/// "does not exist" (404) from "belongs to another house" (403, audited) — then loads
/// the entity through the tenant-filtered set (second, independent check).
/// </summary>
public sealed class TenantResourceGuard(
    IAppDbContext db,
    ITenantContext tenant,
    IAuditLogger audit,
    ILogger<TenantResourceGuard> logger)
{
    public async Task<Comic> GetComicAsync(Guid comicId, CancellationToken ct, bool includeTags = false)
    {
        var owner = await db.Comics.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => c.Id == comicId)
            .Select(c => (Guid?)c.TranslationHouseId)
            .FirstOrDefaultAsync(ct);

        await EnsureOwnedAsync(owner, "comic", comicId, "Truyện", ct);

        var query = db.Comics.Where(c => c.Id == comicId);
        if (includeTags)
        {
            query = query.Include(c => c.Tags).ThenInclude(t => t.Tag);
        }

        return await query.FirstOrDefaultAsync(ct) ?? throw Errors.NotFound("Truyện");
    }

    public async Task<Chapter> GetChapterAsync(Guid chapterId, CancellationToken ct)
    {
        var owner = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(ch => ch.Id == chapterId)
            .Select(ch => (Guid?)ch.Comic!.TranslationHouseId)
            .FirstOrDefaultAsync(ct);

        await EnsureOwnedAsync(owner, "chapter", chapterId, "Chương", ct);

        return await db.Chapters.Include(ch => ch.Comic).FirstOrDefaultAsync(ch => ch.Id == chapterId, ct)
               ?? throw Errors.NotFound("Chương");
    }

    private async Task EnsureOwnedAsync(Guid? ownerHouseId, string type, Guid id, string label, CancellationToken ct)
    {
        if (!tenant.IsResolved)
        {
            throw Errors.Forbidden("no_house_context", "Không xác định được nhóm dịch.");
        }

        if (ownerHouseId is null)
        {
            throw Errors.NotFound(label);
        }

        if (ownerHouseId != tenant.HouseId)
        {
            logger.LogWarning("Cross-tenant access denied: user {UserId} (house {HouseId}) -> {Type} {ResourceId} of house {OwnerHouseId}",
                tenant.UserId, tenant.HouseId, type, id, ownerHouseId);
            await audit.WriteAsync(AuditActions.CrossTenantDenied, type, id, tenant.HouseId, new { ownerHouseId }, success: false, ct: ct);
            throw Errors.Forbidden("cross_tenant", "Bạn không có quyền truy cập dữ liệu của nhóm dịch khác.");
        }
    }
}
