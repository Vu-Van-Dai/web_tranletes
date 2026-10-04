using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Identity;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Domain.Common;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Tenancy;

public sealed record HouseProfileDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string Status,
    HouseLinksDto Links,
    string MyRole,
    IReadOnlyList<string> MyPermissions,
    DateTimeOffset CreatedAt);

public sealed record UpdateHouseProfileRequest(string? Name, string? Slug, string? Description, HouseLinksDto? Links);

public sealed class HouseProfileService(
    IAppDbContext db,
    ITenantContext tenant,
    IMediaService media,
    IPublicUrls urls,
    StorageOutbox outbox,
    IAuditLogger audit,
    TimeProvider clock)
{
    public async Task<HouseProfileDto> GetAsync(CancellationToken ct)
    {
        var house = await db.TranslationHouses.AsNoTracking().FirstAsync(h => h.Id == tenant.HouseId, ct);
        return Map(house);
    }

    public async Task<HouseProfileDto> UpdateAsync(UpdateHouseProfileRequest request, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstAsync(h => h.Id == tenant.HouseId, ct);
        var (name, slug, description, links) = await HouseRules.ValidateAsync(db, house.Id, request.Name, request.Slug ?? house.Slug, request.Description, request.Links, ct);
        house.UpdateProfile(name, slug, description, links, clock.GetUtcNow());
        audit.Add(AuditActions.HouseProfileUpdated, "house", house.Id, house.Id, new { name, slug });
        await db.SaveChangesAsync(ct);
        return Map(house);
    }

    public async Task<HouseProfileDto> SetLogoAsync(Stream image, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstAsync(h => h.Id == tenant.HouseId, ct);
        var stored = await media.StoreLogoAsync(house.Id, image, ct);
        var old = house.LogoKey;
        house.SetLogo(stored.Key, clock.GetUtcNow());
        if (old is not null && old != stored.Key)
        {
            outbox.Withdraw(old, null);
        }

        await db.SaveChangesAsync(ct);
        return Map(house);
    }

    private HouseProfileDto Map(TranslationHouse h) => new(
        h.Id, h.Name, h.Slug, h.Description, urls.ObjectUrl(h.LogoKey), ApiText.Enum(h.Status),
        new HouseLinksDto(h.Links.Facebook, h.Links.Discord, h.Links.Website),
        ApiText.Enum(tenant.Role), PermissionNames.ToNames(tenant.Permissions), h.CreatedAt);
}

/// <summary>Validation shared by house creation (admin) and profile editing (owner).</summary>
public static class HouseRules
{
    public static async Task<(string Name, string Slug, string? Description, HouseLinks Links)> ValidateAsync(
        IAppDbContext db, Guid? houseId, string? name, string? slug, string? description, HouseLinksDto? links, CancellationToken ct)
    {
        var v = new Validator();
        v.Required("name", name, "Tên nhóm dịch").Length("name", name, "Tên nhóm dịch", 2, 100);
        var finalSlug = string.IsNullOrWhiteSpace(slug) ? TextNormalizer.ToSlug(name, 100, "nhom-dich") : slug.Trim().ToLowerInvariant();
        v.Slug("slug", finalSlug).Length("slug", finalSlug, "Đường dẫn", 2, 100);
        v.MaxLength("description", description, "Giới thiệu", 2000);
        v.Url("links.facebook", links?.Facebook, "Liên kết Facebook");
        v.Url("links.discord", links?.Discord, "Liên kết Discord");
        v.Url("links.website", links?.Website, "Trang web");
        v.ThrowIfInvalid();

        var trimmedName = name!.Trim();
        if (await db.TranslationHouses.AnyAsync(h => h.Id != houseId && h.Name == trimmedName, ct))
        {
            v.Add("name", "Tên nhóm dịch đã tồn tại.");
        }

        if (await db.TranslationHouses.AnyAsync(h => h.Id != houseId && h.Slug == finalSlug, ct))
        {
            v.Add("slug", "Đường dẫn đã được nhóm dịch khác sử dụng.");
        }

        v.ThrowIfInvalid();
        return (trimmedName, finalSlug, string.IsNullOrWhiteSpace(description) ? null : description.Trim(), new HouseLinks
        {
            Facebook = Clean(links?.Facebook),
            Discord = Clean(links?.Discord),
            Website = Clean(links?.Website),
        });
    }

    private static string? Clean(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
