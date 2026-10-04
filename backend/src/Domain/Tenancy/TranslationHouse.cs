namespace ComicPlatform.Domain.Tenancy;

public enum HouseStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>Public contact links of a translation house (stored as JSON).</summary>
public sealed class HouseLinks
{
    public string? Facebook { get; set; }
    public string? Discord { get; set; }
    public string? Website { get; set; }
}

public sealed class TranslationHouse
{
    private TranslationHouse()
    {
    }

    public TranslationHouse(string name, string slug, string? description, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Name = name.Trim();
        Slug = slug;
        Description = description;
        Status = HouseStatus.Active;
        Links = new HouseLinks();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? LogoKey { get; private set; }
    public HouseLinks Links { get; private set; } = new();
    public HouseStatus Status { get; private set; }
    public string? SuspendedReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsActive => Status == HouseStatus.Active;

    public void UpdateProfile(string name, string slug, string? description, HouseLinks links, DateTimeOffset now)
    {
        Name = name.Trim();
        Slug = slug;
        Description = description;
        Links = new HouseLinks { Facebook = links.Facebook, Discord = links.Discord, Website = links.Website };
        UpdatedAt = now;
    }

    public void SetLogo(string? logoKey, DateTimeOffset now)
    {
        LogoKey = logoKey;
        UpdatedAt = now;
    }

    public void Suspend(string? reason, DateTimeOffset now)
    {
        Status = HouseStatus.Suspended;
        SuspendedReason = reason;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = HouseStatus.Active;
        SuspendedReason = null;
        UpdatedAt = now;
    }
}
