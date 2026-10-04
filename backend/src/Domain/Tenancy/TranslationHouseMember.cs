namespace ComicPlatform.Domain.Tenancy;

public enum HouseRole
{
    Owner = 0,
    Staff = 1,
}

public enum MemberStatus
{
    Active = 0,
    Removed = 1,
}

/// <summary>
/// Fine-grained house permissions. Owners implicitly hold <see cref="All"/>;
/// staff hold only the delegable subset the owner assigned.
/// </summary>
[Flags]
public enum HousePermissions
{
    None = 0,
    ViewMembers = 1 << 0,
    ManageComics = 1 << 1,
    ManageChapters = 1 << 2,
    UploadChapters = 1 << 3,
    PublishChapters = 1 << 4,

    // Owner-only (never delegable to staff)
    ManageMembers = 1 << 10,
    ManageProfile = 1 << 11,

    Delegable = ViewMembers | ManageComics | ManageChapters | UploadChapters | PublishChapters,
    All = Delegable | ManageMembers | ManageProfile,

    StaffDefault = ViewMembers | ManageChapters | UploadChapters,
}

public sealed class TranslationHouseMember
{
    private TranslationHouseMember()
    {
    }

    public TranslationHouseMember(Guid houseId, Guid userId, HouseRole role, HousePermissions permissions, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TranslationHouseId = houseId;
        UserId = userId;
        Role = role;
        Permissions = role == HouseRole.Owner ? HousePermissions.All : permissions & HousePermissions.Delegable;
        Status = MemberStatus.Active;
        JoinedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid TranslationHouseId { get; private set; }
    public Guid UserId { get; private set; }
    public HouseRole Role { get; private set; }
    public HousePermissions Permissions { get; private set; }
    public MemberStatus Status { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public TranslationHouse? TranslationHouse { get; private set; }
    public Identity.User? User { get; private set; }

    public bool IsActive => Status == MemberStatus.Active;

    public HousePermissions EffectivePermissions =>
        Role == HouseRole.Owner ? HousePermissions.All : Permissions & HousePermissions.Delegable;

    public bool Has(HousePermissions permission) => (EffectivePermissions & permission) == permission;

    public void Update(HouseRole role, HousePermissions permissions, DateTimeOffset now)
    {
        Role = role;
        Permissions = role == HouseRole.Owner ? HousePermissions.All : permissions & HousePermissions.Delegable;
        UpdatedAt = now;
    }

    public void Remove(DateTimeOffset now)
    {
        Status = MemberStatus.Removed;
        UpdatedAt = now;
    }

    public void Reactivate(HouseRole role, HousePermissions permissions, DateTimeOffset now)
    {
        Status = MemberStatus.Active;
        Update(role, permissions, now);
        JoinedAt = now;
    }
}
