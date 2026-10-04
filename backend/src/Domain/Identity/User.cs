namespace ComicPlatform.Domain.Identity;

public enum SystemRole
{
    User = 0,
    SystemAdmin = 1,
}

public enum UserStatus
{
    Active = 0,
    Disabled = 1,
}

public sealed class User
{
    private User()
    {
    }

    public User(string username, string email, string displayName, string passwordHash, SystemRole role, DateTimeOffset now, bool mustChangePassword = false)
    {
        Id = Guid.CreateVersion7();
        Username = username.Trim();
        NormalizedUsername = Normalize(username);
        Email = email.Trim();
        NormalizedEmail = Normalize(email);
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        SystemRole = role;
        Status = UserStatus.Active;
        MustChangePassword = mustChangePassword;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string NormalizedUsername { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public SystemRole SystemRole { get; private set; }
    public UserStatus Status { get; private set; }
    public bool MustChangePassword { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public bool IsActive => Status == UserStatus.Active;

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && until > now;

    public void RecordFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutUntil = now + lockoutDuration;
            FailedLoginCount = 0;
        }

        UpdatedAt = now;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void SetPassword(string passwordHash, bool mustChangePassword, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        FailedLoginCount = 0;
        LockoutUntil = null;
        UpdatedAt = now;
    }

    /// <summary>Silent hash upgrade (e.g. stronger Argon2 parameters) without touching flags.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void UpdateDisplayName(string displayName, DateTimeOffset now)
    {
        DisplayName = displayName.Trim();
        UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        Status = UserStatus.Disabled;
        UpdatedAt = now;
    }

    public void Enable(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        FailedLoginCount = 0;
        LockoutUntil = null;
        UpdatedAt = now;
    }

    public void PromoteToSystemAdmin(DateTimeOffset now)
    {
        SystemRole = SystemRole.SystemAdmin;
        UpdatedAt = now;
    }
}
