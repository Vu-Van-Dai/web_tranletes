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
    DateTimeOffset CreatedAt,
    string? CoverUrl,
    string? Tagline,
    string? ContactEmail,
    int? FoundedYear,
    bool Recruiting,
    string? RecruitmentInfo);

public sealed record UpdateHouseProfileRequest(
    string? Name,
    string? Slug,
    string? Description,
    HouseLinksDto? Links,
    string? Tagline = null,
    string? ContactEmail = null,
    int? FoundedYear = null,
    bool Recruiting = false,
    string? RecruitmentInfo = null);

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
        var details = HouseRules.ValidateDetails(request, clock.GetUtcNow().Year);
        var (name, slug, description, links) = await HouseRules.ValidateAsync(db, house.Id, request.Name, request.Slug ?? house.Slug, request.Description, request.Links, ct);
        house.UpdateProfile(name, slug, description, links, clock.GetUtcNow());
        house.UpdateDetails(details, clock.GetUtcNow());
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

    /// <summary>Wide banner of the public house page (converted to WebP, max 1640×624).</summary>
    public async Task<HouseProfileDto> SetCoverAsync(Stream image, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstAsync(h => h.Id == tenant.HouseId, ct);
        var stored = await media.StoreHouseCoverAsync(house.Id, image, ct);
        var old = house.CoverKey;
        house.SetCover(stored.Key, clock.GetUtcNow());
        if (old is not null && old != stored.Key)
        {
            outbox.Withdraw(old, null);
        }

        audit.Add(AuditActions.HouseProfileUpdated, "house", house.Id, house.Id, new { cover = stored.Key });
        await db.SaveChangesAsync(ct);
        return Map(house);
    }

    public async Task<HouseProfileDto> RemoveCoverAsync(CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstAsync(h => h.Id == tenant.HouseId, ct);
        if (house.CoverKey is { } old)
        {
            house.SetCover(null, clock.GetUtcNow());
            outbox.Withdraw(old, null);
            audit.Add(AuditActions.HouseProfileUpdated, "house", house.Id, house.Id, new { cover = (string?)null });
            await db.SaveChangesAsync(ct);
        }

        return Map(house);
    }

    private HouseProfileDto Map(TranslationHouse h) => new(
        h.Id, h.Name, h.Slug, h.Description, urls.ObjectUrl(h.LogoKey), ApiText.Enum(h.Status),
        HouseLinksDto.From(h.Links),
        ApiText.Enum(tenant.Role), PermissionNames.ToNames(tenant.Permissions), h.CreatedAt,
        urls.ObjectUrl(h.CoverKey), h.Tagline, h.ContactEmail, h.FoundedYear, h.Recruiting, h.RecruitmentInfo);
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
        v.Url("links.tiktok", links?.Tiktok, "Liên kết TikTok");
        v.Url("links.youtube", links?.Youtube, "Liên kết YouTube");
        v.Url("links.donate", links?.Donate, "Liên kết ủng hộ");
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
            Tiktok = Clean(links?.Tiktok),
            Youtube = Clean(links?.Youtube),
            Donate = Clean(links?.Donate),
        });
    }

    public static HouseDetails ValidateDetails(UpdateHouseProfileRequest request, int currentYear)
    {
        var v = new Validator();
        v.MaxLength("tagline", request.Tagline, "Khẩu hiệu", TranslationHouse.TaglineMaxLength);
        v.MaxLength("recruitmentInfo", request.RecruitmentInfo, "Thông tin tuyển thành viên", TranslationHouse.RecruitmentInfoMaxLength);
        if (!string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            v.Email("contactEmail", request.ContactEmail.Trim());
        }

        if (request.FoundedYear is { } year)
        {
            v.Must(year >= 1990 && year <= currentYear, "foundedYear", $"Năm thành lập phải từ 1990 đến {currentYear}.");
        }

        v.ThrowIfInvalid();
        return new HouseDetails(
            Clean(request.Tagline),
            Clean(request.ContactEmail)?.ToLowerInvariant(),
            request.FoundedYear,
            request.Recruiting,
            Clean(request.RecruitmentInfo));
    }

    private static string? Clean(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
