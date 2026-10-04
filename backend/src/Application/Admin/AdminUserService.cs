using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Admin;

public sealed record AdminUserItem(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string SystemRole,
    string Status,
    bool MustChangePassword,
    int HouseCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record ResetPasswordResult(string TemporaryPassword);

public sealed class AdminUserService(
    IAppDbContext db,
    IPasswordHasher hasher,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<AdminUserService> logger)
{
    public async Task<PagedResult<AdminUserItem>> ListAsync(string? q, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Normalize(page, pageSize, 20, 100);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = "%" + HouseComicService.EscapeLike(q.Trim()) + "%";
            query = query.Where(u => EF.Functions.ILike(u.Username, term, "\\") || EF.Functions.ILike(u.Email, term, "\\") || EF.Functions.ILike(u.DisplayName, term, "\\"));
        }

        if (ApiText.TryParseEnum<UserStatus>(status, out var st))
        {
            query = query.Where(u => u.Status == st);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.CreatedAt).Skip(paging.Skip).Take(paging.PageSize)
            .Select(u => new
            {
                u.Id, u.Username, u.Email, u.DisplayName, u.SystemRole, u.Status, u.MustChangePassword, u.CreatedAt, u.LastLoginAt,
                Houses = db.TranslationHouseMembers.Count(m => m.UserId == u.Id && m.Status == MemberStatus.Active),
            })
            .ToListAsync(ct);

        return new PagedResult<AdminUserItem>(rows.Select(u => new AdminUserItem(u.Id, u.Username, u.Email, u.DisplayName,
            u.SystemRole == SystemRole.SystemAdmin ? "systemAdmin" : "user", ApiText.Enum(u.Status), u.MustChangePassword, u.Houses, u.CreatedAt, u.LastLoginAt)).ToList(),
            paging.Page, paging.PageSize, total);
    }

    public async Task DisableAsync(Guid userId, CancellationToken ct)
    {
        if (userId == currentUser.UserId)
        {
            throw Errors.Conflict("cannot_disable_self", "Bạn không thể tự vô hiệu hoá tài khoản của mình.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Errors.NotFound("Tài khoản");
        var now = clock.GetUtcNow();
        user.Disable(now);
        await RevokeSessionsAsync(userId, now, "disabled", ct);
        audit.Add(AuditActions.UserDisabled, "user", userId, null, new { user.Username });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("User {UserId} disabled by admin", userId);
    }

    public async Task EnableAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Errors.NotFound("Tài khoản");
        user.Enable(clock.GetUtcNow());
        audit.Add(AuditActions.UserEnabled, "user", userId, null, new { user.Username });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Errors.NotFound("Tài khoản");
        var temp = AdminHouseService.GeneratePassword();
        var now = clock.GetUtcNow();
        user.SetPassword(await hasher.HashAsync(temp, ct), mustChangePassword: true, now);
        await RevokeSessionsAsync(userId, now, "password_reset", ct);
        audit.Add(AuditActions.UserPasswordReset, "user", userId, null, new { user.Username });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Password of user {UserId} reset by admin", userId);
        return new ResetPasswordResult(temp);
    }

    private async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, string reason, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens)
        {
            t.Revoke(now, reason);
        }
    }
}
