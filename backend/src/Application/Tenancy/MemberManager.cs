using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Identity;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Tenancy;

public sealed record MemberDto(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    string Role,
    IReadOnlyList<string> Permissions,
    string Status,
    DateTimeOffset JoinedAt);

public sealed record AddMemberRequest(string? Login, string? Role, IReadOnlyList<string>? Permissions);

public sealed record UpdateMemberRequest(string? Role, IReadOnlyList<string>? Permissions);

/// <summary>
/// Membership rules shared by the house owner UI and the SystemAdmin UI. The house id is always
/// supplied by the server (tenant context or admin route), never by the request body.
/// </summary>
public sealed class MemberManager(IAppDbContext db, IAuditLogger audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<MemberDto>> ListAsync(Guid houseId, bool includeEmail, CancellationToken ct)
    {
        var rows = await db.TranslationHouseMembers.AsNoTracking()
            .Where(m => m.TranslationHouseId == houseId && m.Status == MemberStatus.Active)
            .OrderBy(m => m.Role).ThenBy(m => m.JoinedAt)
            .Select(m => new { m.Id, m.UserId, m.User!.Username, m.User.DisplayName, m.User.Email, m.Role, m.Permissions, m.Status, m.JoinedAt })
            .ToListAsync(ct);

        return rows.Select(m => new MemberDto(m.Id, m.UserId, m.Username, m.DisplayName, includeEmail ? m.Email : null,
            ApiText.Enum(m.Role), PermissionNames.ToNames(m.Role == HouseRole.Owner ? HousePermissions.All : m.Permissions & HousePermissions.Delegable),
            ApiText.Enum(m.Status), m.JoinedAt)).ToList();
    }

    public async Task<MemberDto> AddAsync(Guid houseId, AddMemberRequest request, CancellationToken ct)
    {
        var v = new Validator();
        v.Required("login", request.Login, "Email hoặc tên đăng nhập");
        var (role, permissions) = ParseRole(request.Role, request.Permissions, v);
        v.ThrowIfInvalid();

        var login = User.Normalize(request.Login!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == login || u.NormalizedUsername == login, ct)
                   ?? throw Errors.Field("login", "Không tìm thấy tài khoản với email/tên đăng nhập này.");
        if (!user.IsActive)
        {
            throw Errors.Field("login", "Tài khoản này đang bị vô hiệu hoá.");
        }

        var now = clock.GetUtcNow();
        var existing = await db.TranslationHouseMembers.FirstOrDefaultAsync(m => m.TranslationHouseId == houseId && m.UserId == user.Id, ct);
        if (existing is { IsActive: true })
        {
            throw Errors.Conflict("already_member", "Tài khoản này đã là thành viên của nhóm.");
        }

        TranslationHouseMember member;
        if (existing is not null)
        {
            existing.Reactivate(role, permissions, now);
            member = existing;
        }
        else
        {
            member = new TranslationHouseMember(houseId, user.Id, role, permissions, now);
            db.TranslationHouseMembers.Add(member);
        }

        audit.Add(AuditActions.MemberAdded, "member", member.Id, houseId, new { user.Username, Role = role.ToString() });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(houseId, true, ct)).First(m => m.Id == member.Id);
    }

    public async Task<MemberDto> UpdateAsync(Guid houseId, Guid memberId, UpdateMemberRequest request, CancellationToken ct)
    {
        var member = await GetMemberAsync(houseId, memberId, ct);
        var v = new Validator();
        var (role, permissions) = ParseRole(request.Role, request.Permissions, v);
        v.ThrowIfInvalid();

        if (member.Role == HouseRole.Owner && role != HouseRole.Owner)
        {
            await EnsureAnotherOwnerAsync(houseId, member.Id, ct);
        }

        member.Update(role, permissions, clock.GetUtcNow());
        audit.Add(AuditActions.MemberUpdated, "member", member.Id, houseId, new { Role = role.ToString(), Permissions = PermissionNames.ToNames(permissions) });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(houseId, true, ct)).First(m => m.Id == member.Id);
    }

    public async Task RemoveAsync(Guid houseId, Guid memberId, CancellationToken ct)
    {
        var member = await GetMemberAsync(houseId, memberId, ct);
        if (member.Role == HouseRole.Owner)
        {
            await EnsureAnotherOwnerAsync(houseId, member.Id, ct);
        }

        member.Remove(clock.GetUtcNow());
        audit.Add(AuditActions.MemberRemoved, "member", member.Id, houseId, new { member.UserId });
        await db.SaveChangesAsync(ct);
    }

    private async Task<TranslationHouseMember> GetMemberAsync(Guid houseId, Guid memberId, CancellationToken ct)
    {
        var member = await db.TranslationHouseMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.Status == MemberStatus.Active, ct)
                     ?? throw Errors.NotFound("Thành viên");
        if (member.TranslationHouseId != houseId)
        {
            await audit.WriteAsync(AuditActions.CrossTenantDenied, "member", memberId, houseId, new { ownerHouseId = member.TranslationHouseId }, success: false, ct: ct);
            throw Errors.Forbidden("cross_tenant", "Bạn không có quyền truy cập dữ liệu của nhóm dịch khác.");
        }

        return member;
    }

    private async Task EnsureAnotherOwnerAsync(Guid houseId, Guid exceptMemberId, CancellationToken ct)
    {
        var others = await db.TranslationHouseMembers.CountAsync(m =>
            m.TranslationHouseId == houseId && m.Id != exceptMemberId && m.Status == MemberStatus.Active && m.Role == HouseRole.Owner, ct);
        if (others == 0)
        {
            throw Errors.Conflict("last_owner", "Nhóm dịch phải có ít nhất một chủ nhóm.");
        }
    }

    private static (HouseRole Role, HousePermissions Permissions) ParseRole(string? roleText, IReadOnlyList<string>? permissionNames, Validator v)
    {
        var role = HouseRole.Staff;
        if (roleText is not null && !ApiText.TryParseEnum(roleText, out role))
        {
            v.Add("role", "Vai trò không hợp lệ.");
        }

        var permissions = HousePermissions.StaffDefault;
        if (permissionNames is not null)
        {
            permissions = PermissionNames.Parse(permissionNames, out var unknown);
            if (unknown.Count > 0)
            {
                v.Add("permissions", "Quyền không hợp lệ: " + string.Join(", ", unknown));
            }
        }

        return (role, permissions & HousePermissions.Delegable);
    }
}
