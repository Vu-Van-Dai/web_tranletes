using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Tenancy;

/// <summary>
/// Resolves who may act and for which house. Nothing here trusts the client: user status, system role
/// and memberships are always re-read from the database. The optional X-House-Id header is only a
/// selector among the caller's own active memberships.
/// </summary>
public sealed class HouseAccessService(
    IAppDbContext db,
    TenantContext tenant,
    IAuditLogger audit,
    ILogger<HouseAccessService> logger)
{
    public async Task ResolveHouseContextAsync(Guid userId, string? requestedHouseId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.MustChangePassword })
            .FirstOrDefaultAsync(ct);

        EnsureUsable(user?.Status, user?.MustChangePassword);

        var memberships = await db.TranslationHouseMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            .Select(m => new { m.Id, m.TranslationHouseId, m.Role, m.Permissions, HouseStatus = m.TranslationHouse!.Status })
            .ToListAsync(ct);

        var selected = memberships.Count == 1 ? memberships[0] : null;
        if (!string.IsNullOrWhiteSpace(requestedHouseId))
        {
            if (!Guid.TryParse(requestedHouseId, out var houseId))
            {
                throw Errors.BadRequest("invalid_house_selector", "Mã nhóm dịch không hợp lệ.");
            }

            selected = memberships.FirstOrDefault(m => m.TranslationHouseId == houseId);
            if (selected is null)
            {
                logger.LogWarning("House selector mismatch: user {UserId} is not a member of {HouseId}", userId, houseId);
                await audit.WriteAsync(AuditActions.HouseSelectorMismatch, "house", houseId, houseId, success: false, ct: ct);
                throw Errors.Forbidden("not_house_member", "Bạn không phải thành viên của nhóm dịch này.");
            }
        }
        else if (memberships.Count == 0)
        {
            throw Errors.Forbidden("no_house_membership", "Tài khoản của bạn chưa thuộc nhóm dịch nào.");
        }
        else if (selected is null)
        {
            throw Errors.Conflict("house_selection_required", "Vui lòng chọn nhóm dịch muốn làm việc.");
        }

        if (selected.HouseStatus != HouseStatus.Active)
        {
            throw Errors.Forbidden("house_suspended", "Nhóm dịch đang bị tạm ngưng hoạt động.");
        }

        var effective = selected.Role == HouseRole.Owner ? HousePermissions.All : selected.Permissions & HousePermissions.Delegable;
        tenant.Set(selected.TranslationHouseId, userId, selected.Id, selected.Role, effective);
    }

    public async Task EnsureSystemAdminAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.MustChangePassword, u.SystemRole })
            .FirstOrDefaultAsync(ct);

        EnsureUsable(user?.Status, user?.MustChangePassword);
        if (user!.SystemRole != SystemRole.SystemAdmin)
        {
            logger.LogWarning("Admin access denied for user {UserId}", userId);
            throw Errors.Forbidden("admin_only", "Chức năng chỉ dành cho quản trị hệ thống.");
        }
    }

    public void EnsurePermission(HousePermissions permission)
    {
        if (!tenant.IsResolved)
        {
            throw Errors.Forbidden("no_house_context", "Không xác định được nhóm dịch.");
        }

        if ((tenant.Permissions & permission) != permission)
        {
            logger.LogWarning("Permission {Permission} denied for member {MemberId} in house {HouseId}", permission, tenant.MemberId, tenant.HouseId);
            throw Errors.Forbidden("missing_permission", "Bạn chưa được cấp quyền cho thao tác này trong nhóm dịch.");
        }
    }

    private static void EnsureUsable(UserStatus? status, bool? mustChangePassword)
    {
        if (status is null)
        {
            throw Errors.Unauthorized();
        }

        if (status != UserStatus.Active)
        {
            throw Errors.Forbidden("account_disabled", "Tài khoản đã bị vô hiệu hoá.");
        }

        if (mustChangePassword == true)
        {
            throw Errors.Forbidden("password_change_required", "Bạn cần đổi mật khẩu trước khi tiếp tục.");
        }
    }
}
