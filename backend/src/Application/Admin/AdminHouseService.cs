using System.Security.Cryptography;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Admin;

public sealed record AdminHouseListItem(
    Guid Id, string Name, string Slug, string? LogoUrl, string Status, int MemberCount, int ComicCount, int PublishedChapterCount, DateTimeOffset CreatedAt);

public sealed record HouseOwnerDto(Guid UserId, string Username, string DisplayName, string Email);

public sealed record AdminHouseDetail(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string Status,
    string? SuspendedReason,
    HouseLinksDto Links,
    int MemberCount,
    int ComicCount,
    int ChapterCount,
    int PublishedChapterCount,
    long StorageBytes,
    IReadOnlyList<HouseOwnerDto> Owners,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record NewOwnerAccount(string? Username, string? Email, string? DisplayName, string? TemporaryPassword);

public sealed record CreateHouseOwner(string? ExistingLogin, NewOwnerAccount? NewAccount);

public sealed record CreateHouseRequest(string? Name, string? Slug, string? Description, HouseLinksDto? Links, CreateHouseOwner? Owner);

public sealed record CreateHouseResult(AdminHouseDetail House, Guid OwnerUserId, string? GeneratedPassword);

public sealed record UpdateHouseRequest(string? Name, string? Slug, string? Description, HouseLinksDto? Links);

public sealed record SuspendHouseRequest(string? Reason);

public sealed record AdminHouseComicItem(
    Guid Id,
    string TranslatedTitle,
    string OriginalTitle,
    string Status,
    bool IsMature,
    int ChapterCount,
    int PublishedChapterCount,
    long StorageBytes,
    DateTimeOffset? LastChapterPublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// System-level management of translation houses. Reads content metadata across tenants
/// (explicitly opting out of the tenant filter) but never page/image data.
/// </summary>
public sealed class AdminHouseService(
    IAppDbContext db,
    IPasswordHasher hasher,
    MemberManager members,
    IPublicUrls urls,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<AdminHouseService> logger)
{
    public async Task<PagedResult<AdminHouseListItem>> ListAsync(string? q, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Normalize(page, pageSize, 20, 100);
        var query = db.TranslationHouses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = "%" + HouseComicService.EscapeLike(q.Trim()) + "%";
            query = query.Where(h => EF.Functions.ILike(h.Name, term, "\\") || EF.Functions.ILike(h.Slug, term, "\\"));
        }

        if (ApiText.TryParseEnum<HouseStatus>(status, out var st))
        {
            query = query.Where(h => h.Status == st);
        }

        var total = await query.CountAsync(ct);
        var comics = db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]);
        var rows = await query.OrderByDescending(h => h.CreatedAt).Skip(paging.Skip).Take(paging.PageSize)
            .Select(h => new
            {
                h.Id, h.Name, h.Slug, h.LogoKey, h.Status, h.CreatedAt,
                MemberCount = db.TranslationHouseMembers.Count(m => m.TranslationHouseId == h.Id && m.Status == MemberStatus.Active),
                ComicCount = comics.Count(c => c.TranslationHouseId == h.Id),
                Published = comics.Where(c => c.TranslationHouseId == h.Id).Sum(c => (int?)c.PublishedChapterCount) ?? 0,
            })
            .ToListAsync(ct);

        return new PagedResult<AdminHouseListItem>(rows.Select(r => new AdminHouseListItem(r.Id, r.Name, r.Slug, urls.ObjectUrl(r.LogoKey),
            ApiText.Enum(r.Status), r.MemberCount, r.ComicCount, r.Published, r.CreatedAt)).ToList(), paging.Page, paging.PageSize, total);
    }

    public async Task<CreateHouseResult> CreateAsync(CreateHouseRequest request, CancellationToken ct)
    {
        var (name, slug, description, links) = await HouseRules.ValidateAsync(db, null, request.Name, request.Slug, request.Description, request.Links, ct);
        var now = clock.GetUtcNow();
        var owner = request.Owner;
        var v = new Validator();
        if (owner is null || (string.IsNullOrWhiteSpace(owner.ExistingLogin) && owner.NewAccount is null))
        {
            v.Add("owner", "Cần chỉ định chủ nhóm (tài khoản có sẵn hoặc tạo tài khoản mới).");
            v.ThrowIfInvalid();
        }

        User ownerUser;
        string? generatedPassword = null;
        if (!string.IsNullOrWhiteSpace(owner!.ExistingLogin))
        {
            var login = User.Normalize(owner.ExistingLogin);
            ownerUser = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == login || u.NormalizedUsername == login, ct)
                        ?? throw Errors.Field("owner.existingLogin", "Không tìm thấy tài khoản với email/tên đăng nhập này.");
            if (!ownerUser.IsActive)
            {
                throw Errors.Field("owner.existingLogin", "Tài khoản này đang bị vô hiệu hoá.");
            }
        }
        else
        {
            var acc = owner.NewAccount!;
            var username = acc.Username?.Trim().ToLowerInvariant();
            v.Required("owner.newAccount.username", username, "Tên đăng nhập").Username("owner.newAccount.username", username);
            v.Required("owner.newAccount.email", acc.Email, "Email").Email("owner.newAccount.email", acc.Email);
            v.Required("owner.newAccount.displayName", acc.DisplayName, "Tên hiển thị").Length("owner.newAccount.displayName", acc.DisplayName, "Tên hiển thị", 2, 64);
            if (!string.IsNullOrEmpty(acc.TemporaryPassword))
            {
                v.Password("owner.newAccount.temporaryPassword", acc.TemporaryPassword, "Mật khẩu tạm");
            }

            v.ThrowIfInvalid();
            var normalizedEmail = User.Normalize(acc.Email!);
            if (await db.Users.AnyAsync(u => u.NormalizedUsername == username, ct))
            {
                v.Add("owner.newAccount.username", "Tên đăng nhập đã được sử dụng.");
            }

            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            {
                v.Add("owner.newAccount.email", "Email đã được sử dụng.");
            }

            v.ThrowIfInvalid();
            var password = acc.TemporaryPassword;
            if (string.IsNullOrEmpty(password))
            {
                password = generatedPassword = GeneratePassword();
            }

            ownerUser = new User(username!, acc.Email!, acc.DisplayName!, await hasher.HashAsync(password, ct), SystemRole.User, now, mustChangePassword: true);
            db.Users.Add(ownerUser);
            audit.Add(AuditActions.UserCreated, "user", ownerUser.Id, null, new { ownerUser.Username, reason = "house_owner" });
        }

        var house = new TranslationHouse(name, slug, description, now);
        house.UpdateProfile(name, slug, description, links, now);
        db.TranslationHouses.Add(house);
        db.TranslationHouseMembers.Add(new TranslationHouseMember(house.Id, ownerUser.Id, HouseRole.Owner, HousePermissions.All, now));
        audit.Add(AuditActions.HouseCreated, "house", house.Id, house.Id, new { house.Name, house.Slug, owner = ownerUser.Username });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Translation house {HouseId} created with owner {UserId}", house.Id, ownerUser.Id);
        return new CreateHouseResult(await GetAsync(house.Id, ct), ownerUser.Id, generatedPassword);
    }

    public async Task<AdminHouseDetail> GetAsync(Guid houseId, CancellationToken ct)
    {
        var h = await db.TranslationHouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == houseId, ct) ?? throw Errors.NotFound("Nhóm dịch");
        var comics = db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).Where(c => c.TranslationHouseId == houseId);
        var chapters = db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).Where(c => c.Comic!.TranslationHouseId == houseId);
        var packages = db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).Where(p => p.Chapter!.Comic!.TranslationHouseId == houseId && p.Status != PackageStatus.Failed);

        var owners = await db.TranslationHouseMembers.AsNoTracking()
            .Where(m => m.TranslationHouseId == houseId && m.Status == MemberStatus.Active && m.Role == HouseRole.Owner)
            .Select(m => new HouseOwnerDto(m.UserId, m.User!.Username, m.User.DisplayName, m.User.Email))
            .ToListAsync(ct);

        return new AdminHouseDetail(h.Id, h.Name, h.Slug, h.Description, urls.ObjectUrl(h.LogoKey), ApiText.Enum(h.Status), h.SuspendedReason,
            new HouseLinksDto(h.Links.Facebook, h.Links.Discord, h.Links.Website),
            await db.TranslationHouseMembers.CountAsync(m => m.TranslationHouseId == houseId && m.Status == MemberStatus.Active, ct),
            await comics.CountAsync(ct),
            await chapters.CountAsync(ct),
            await chapters.CountAsync(c => c.Status == ChapterStatus.Published, ct),
            await packages.SumAsync(p => (long?)p.FileSize, ct) ?? 0,
            owners, h.CreatedAt, h.UpdatedAt);
    }

    public async Task<AdminHouseDetail> UpdateAsync(Guid houseId, UpdateHouseRequest request, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstOrDefaultAsync(h => h.Id == houseId, ct) ?? throw Errors.NotFound("Nhóm dịch");
        var (name, slug, description, links) = await HouseRules.ValidateAsync(db, houseId, request.Name, request.Slug ?? house.Slug, request.Description, request.Links, ct);
        house.UpdateProfile(name, slug, description, links, clock.GetUtcNow());
        audit.Add(AuditActions.HouseUpdated, "house", house.Id, house.Id, new { name, slug });
        await db.SaveChangesAsync(ct);
        return await GetAsync(houseId, ct);
    }

    public async Task<AdminHouseDetail> SuspendAsync(Guid houseId, SuspendHouseRequest request, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstOrDefaultAsync(h => h.Id == houseId, ct) ?? throw Errors.NotFound("Nhóm dịch");
        var v = new Validator();
        v.MaxLength("reason", request.Reason, "Lý do", 500);
        v.ThrowIfInvalid();
        house.Suspend(string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), clock.GetUtcNow());
        audit.Add(AuditActions.HouseSuspended, "house", house.Id, house.Id, new { request.Reason });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Translation house {HouseId} suspended", houseId);
        return await GetAsync(houseId, ct);
    }

    public async Task<AdminHouseDetail> ActivateAsync(Guid houseId, CancellationToken ct)
    {
        var house = await db.TranslationHouses.FirstOrDefaultAsync(h => h.Id == houseId, ct) ?? throw Errors.NotFound("Nhóm dịch");
        house.Activate(clock.GetUtcNow());
        audit.Add(AuditActions.HouseActivated, "house", house.Id, house.Id);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Translation house {HouseId} activated", houseId);
        return await GetAsync(houseId, ct);
    }

    public async Task<IReadOnlyList<MemberDto>> MembersAsync(Guid houseId, CancellationToken ct)
    {
        await EnsureExistsAsync(houseId, ct);
        return await members.ListAsync(houseId, includeEmail: true, ct);
    }

    public async Task<MemberDto> AddMemberAsync(Guid houseId, AddMemberRequest request, CancellationToken ct)
    {
        await EnsureExistsAsync(houseId, ct);
        return await members.AddAsync(houseId, request, ct);
    }

    public async Task<MemberDto> UpdateMemberAsync(Guid houseId, Guid memberId, UpdateMemberRequest request, CancellationToken ct)
    {
        await EnsureExistsAsync(houseId, ct);
        return await members.UpdateAsync(houseId, memberId, request, ct);
    }

    public async Task RemoveMemberAsync(Guid houseId, Guid memberId, CancellationToken ct)
    {
        await EnsureExistsAsync(houseId, ct);
        await members.RemoveAsync(houseId, memberId, ct);
    }

    public async Task<PagedResult<AdminHouseComicItem>> ComicsAsync(Guid houseId, string? q, int? page, int? pageSize, CancellationToken ct)
    {
        await EnsureExistsAsync(houseId, ct);
        var paging = PageRequest.Normalize(page, pageSize, 20, 100);
        var query = db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().Where(c => c.TranslationHouseId == houseId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var key = Domain.Common.TextNormalizer.ToSearchKey(q);
            query = query.Where(c => EF.Functions.ILike(c.SearchKey, "%" + HouseComicService.EscapeLike(key) + "%", "\\"));
        }

        var total = await query.CountAsync(ct);
        var chapters = db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]);
        var packages = db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]);
        var rows = await query.OrderByDescending(c => c.UpdatedAt).Skip(paging.Skip).Take(paging.PageSize)
            .Select(c => new
            {
                c.Id, c.TranslatedTitle, c.OriginalTitle, c.Status, c.IsMature, c.PublishedChapterCount, c.LastChapterPublishedAt, c.CreatedAt, c.UpdatedAt,
                ChapterCount = chapters.Count(ch => ch.ComicId == c.Id),
                Storage = packages.Where(p => p.Chapter!.ComicId == c.Id && p.Status != PackageStatus.Failed).Sum(p => (long?)p.FileSize) ?? 0,
            })
            .ToListAsync(ct);

        return new PagedResult<AdminHouseComicItem>(rows.Select(r => new AdminHouseComicItem(r.Id, r.TranslatedTitle, r.OriginalTitle, ApiText.Enum(r.Status),
            r.IsMature, r.ChapterCount, r.PublishedChapterCount, r.Storage, r.LastChapterPublishedAt, r.CreatedAt, r.UpdatedAt)).ToList(), paging.Page, paging.PageSize, total);
    }

    private async Task EnsureExistsAsync(Guid houseId, CancellationToken ct)
    {
        if (!await db.TranslationHouses.AnyAsync(h => h.Id == houseId, ct))
        {
            throw Errors.NotFound("Nhóm dịch");
        }
    }

    internal static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return "Tmp-" + RandomNumberGenerator.GetString(alphabet, 14);
    }
}
