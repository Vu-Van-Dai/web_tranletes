using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Settings;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Identity;

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IPublicUrls urls,
    SettingsService settings,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (!(await settings.GetAsync(ct)).RegistrationEnabled)
        {
            throw Errors.Forbidden("registration_disabled", "Hệ thống đang tạm khoá đăng ký tài khoản mới.");
        }

        var v = new Validator();
        v.Required("username", request.Username, "Tên đăng nhập").Username("username", request.Username?.Trim().ToLowerInvariant());
        v.Required("email", request.Email, "Email").Email("email", request.Email).MaxLength("email", request.Email, "Email", 320);
        v.Required("displayName", request.DisplayName, "Tên hiển thị").Length("displayName", request.DisplayName, "Tên hiển thị", 2, 64);
        v.Password("password", request.Password);
        v.ThrowIfInvalid();

        var username = request.Username!.Trim().ToLowerInvariant();
        var email = request.Email!.Trim();
        await EnsureUniqueAsync(username, email, ct);

        var now = clock.GetUtcNow();
        var hash = await hasher.HashAsync(request.Password!, ct);
        var user = new User(username, email, request.DisplayName!, hash, SystemRole.User, now);
        user.RecordSuccessfulLogin(now);
        db.Users.Add(user);

        var result = await IssueSessionAsync(user, familyId: Guid.CreateVersion7(), ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User registered {UserId}", user.Id);
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var v = new Validator();
        v.Required("login", request.Login, "Email hoặc tên đăng nhập").MaxLength("login", request.Login, "Email hoặc tên đăng nhập", 320);
        v.Required("password", request.Password, "Mật khẩu").MaxLength("password", request.Password, "Mật khẩu", 128);
        v.ThrowIfInvalid();

        var login = User.Normalize(request.Login!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == login || u.NormalizedUsername == login, ct);
        var invalid = Errors.Unauthorized("invalid_credentials", "Email/tên đăng nhập hoặc mật khẩu không đúng.");
        var now = clock.GetUtcNow();

        if (user is null)
        {
            await hasher.DummyVerifyAsync(request.Password!, ct);
            logger.LogWarning("Login failed: unknown account");
            throw invalid;
        }

        if (user.IsLockedOut(now))
        {
            logger.LogWarning("Login rejected: account locked {UserId}", user.Id);
            throw new AppException(429, "account_locked", "Tài khoản tạm thời bị khoá do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.");
        }

        var check = await hasher.VerifyAsync(user.PasswordHash, request.Password!, ct);
        if (check == PasswordCheck.Failed)
        {
            user.RecordFailedLogin(now, MaxFailedAttempts, LockoutDuration);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Login failed: wrong password {UserId}", user.Id);
            throw invalid;
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login rejected: account disabled {UserId}", user.Id);
            throw Errors.Forbidden("account_disabled", "Tài khoản đã bị vô hiệu hoá. Vui lòng liên hệ quản trị viên.");
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(await hasher.HashAsync(request.Password!, ct));
        }

        user.RecordSuccessfulLogin(now);
        var result = await IssueSessionAsync(user, Guid.CreateVersion7(), ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Login succeeded {UserId}", user.Id);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        var unauthorized = Errors.Unauthorized("invalid_refresh_token", "Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại.");
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200)
        {
            throw unauthorized;
        }

        var now = clock.GetUtcNow();
        var hash = tokens.HashOpaqueToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            throw unauthorized;
        }

        if (stored.IsRevoked)
        {
            // Reuse of a rotated token: assume theft and kill the whole family.
            await RevokeFamilyAsync(stored.FamilyId, now, "reuse_detected", ct);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Refresh token reuse detected {UserId} {FamilyId}", stored.UserId, stored.FamilyId);
            throw unauthorized;
        }

        if (!stored.IsActive(now))
        {
            throw unauthorized;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive)
        {
            stored.Revoke(now, "user_inactive");
            await db.SaveChangesAsync(ct);
            throw unauthorized;
        }

        var result = await IssueSessionAsync(user, stored.FamilyId, ct, replaced: stored);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200)
        {
            return;
        }

        var hash = tokens.HashOpaqueToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow(), "logout", ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Logout {UserId}", stored.UserId);
        }
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var v = new Validator();
        v.Required("currentPassword", request.CurrentPassword, "Mật khẩu hiện tại");
        v.Password("newPassword", request.NewPassword, "Mật khẩu mới");
        v.Must(request.NewPassword != request.CurrentPassword, "newPassword", "Mật khẩu mới phải khác mật khẩu hiện tại.");
        v.ThrowIfInvalid();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Errors.Unauthorized();
        if (!user.IsActive)
        {
            throw Errors.Forbidden("account_disabled", "Tài khoản đã bị vô hiệu hoá.");
        }

        if (await hasher.VerifyAsync(user.PasswordHash, request.CurrentPassword!, ct) == PasswordCheck.Failed)
        {
            throw Errors.Field("currentPassword", "Mật khẩu hiện tại không đúng.");
        }

        var now = clock.GetUtcNow();
        user.SetPassword(await hasher.HashAsync(request.NewPassword!, ct), mustChangePassword: false, now);

        // Sign out every other session.
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active)
        {
            t.Revoke(now, "password_changed");
        }

        var result = await IssueSessionAsync(user, Guid.CreateVersion7(), ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Password changed {UserId}", userId);
        return result;
    }

    public async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Errors.Unauthorized();
        if (!user.IsActive)
        {
            throw Errors.Unauthorized("account_disabled", "Tài khoản đã bị vô hiệu hoá.");
        }

        return await BuildMeAsync(user, ct);
    }

    private async Task<MeResponse> BuildMeAsync(User user, CancellationToken ct)
    {
        var memberships = await db.TranslationHouseMembers.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == MemberStatus.Active)
            .Select(m => new { m.Role, m.Permissions, House = m.TranslationHouse! })
            .OrderBy(m => m.House.Name)
            .ToListAsync(ct);

        return new MeResponse(
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.SystemRole == SystemRole.SystemAdmin ? "systemAdmin" : "user",
            user.MustChangePassword,
            memberships.Select(m => new MembershipDto(
                m.House.Id,
                m.House.Name,
                m.House.Slug,
                urls.ObjectUrl(m.House.LogoKey),
                m.House.Status == HouseStatus.Active ? "active" : "suspended",
                m.Role == HouseRole.Owner ? "owner" : "staff",
                PermissionNames.ToNames(m.Role == HouseRole.Owner ? HousePermissions.All : m.Permissions & HousePermissions.Delegable))).ToList());
    }

    private async Task<AuthResult> IssueSessionAsync(User user, Guid familyId, CancellationToken ct, RefreshToken? replaced = null)
    {
        var now = clock.GetUtcNow();
        var raw = tokens.CreateOpaqueToken();
        var refresh = new RefreshToken(user.Id, familyId, tokens.HashOpaqueToken(raw), now, now + tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        replaced?.Revoke(now, "rotated", refresh.Id);

        var access = tokens.CreateAccessToken(user);
        var me = await BuildMeAsync(user, ct);
        return new AuthResult(access.Token, access.ExpiresAt, me, raw, refresh.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, string reason, CancellationToken ct)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in family)
        {
            t.Revoke(now, reason);
        }
    }

    private async Task EnsureUniqueAsync(string username, string email, CancellationToken ct)
    {
        var normalizedEmail = User.Normalize(email);
        var v = new Validator();
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == username, ct))
        {
            v.Add("username", "Tên đăng nhập đã được sử dụng.");
        }

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
        {
            v.Add("email", "Email đã được sử dụng.");
        }

        v.ThrowIfInvalid();
    }
}
