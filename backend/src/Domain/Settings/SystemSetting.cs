namespace ComicPlatform.Domain.Settings;

/// <summary>Whitelisted runtime settings editable by the SystemAdmin.</summary>
public sealed class SystemSetting
{
    public const string RegistrationEnabled = "registration_enabled";
    public const string Announcement = "announcement";

    public static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        RegistrationEnabled,
        Announcement,
    };

    private SystemSetting()
    {
    }

    public SystemSetting(string key, string valueJson, DateTimeOffset now, Guid? updatedBy)
    {
        Key = key;
        Value = valueJson;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Set(string valueJson, DateTimeOffset now, Guid? updatedBy)
    {
        Value = valueJson;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
