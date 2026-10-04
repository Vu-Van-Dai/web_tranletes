using System.Text.Json;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Auditing;

namespace ComicPlatform.Application.Auditing;

public interface IAuditLogger
{
    /// <summary>Stages an audit entry; it is persisted with the caller's next SaveChanges (same transaction).</summary>
    void Add(string action, string? targetType = null, object? targetId = null, Guid? houseId = null, object? data = null, bool success = true);

    /// <summary>Persists an audit entry immediately (e.g. for denied requests that never reach a handler).</summary>
    Task WriteAsync(string action, string? targetType = null, object? targetId = null, Guid? houseId = null, object? data = null, bool success = true, CancellationToken ct = default);
}

public sealed class AuditLogger(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Add(string action, string? targetType = null, object? targetId = null, Guid? houseId = null, object? data = null, bool success = true)
    {
        db.AuditLogs.Add(new AuditLog(
            clock.GetUtcNow(),
            currentUser.UserId,
            action,
            targetType,
            targetId?.ToString(),
            houseId,
            currentUser.IpAddress,
            data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            success));
    }

    public async Task WriteAsync(string action, string? targetType = null, object? targetId = null, Guid? houseId = null, object? data = null, bool success = true, CancellationToken ct = default)
    {
        Add(action, targetType, targetId, houseId, data, success);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Audit action names (stable strings, used for filtering in the admin UI).</summary>
public static class AuditActions
{
    public const string HouseCreated = "house.created";
    public const string HouseUpdated = "house.updated";
    public const string HouseSuspended = "house.suspended";
    public const string HouseActivated = "house.activated";
    public const string HouseProfileUpdated = "house.profile_updated";
    public const string MemberAdded = "member.added";
    public const string MemberUpdated = "member.updated";
    public const string MemberRemoved = "member.removed";
    public const string UserCreated = "user.created";
    public const string UserDisabled = "user.disabled";
    public const string UserEnabled = "user.enabled";
    public const string UserPasswordReset = "user.password_reset";
    public const string ComicCreated = "comic.created";
    public const string ComicDeleted = "comic.deleted";
    public const string ChapterDeleted = "chapter.deleted";
    public const string ChapterUploaded = "chapter.uploaded";
    public const string ChapterPublished = "chapter.published";
    public const string ChapterUnpublished = "chapter.unpublished";
    public const string ChapterRetried = "chapter.retried";
    public const string SettingsUpdated = "settings.updated";
    public const string CrossTenantDenied = "security.cross_tenant_denied";
    public const string HouseSelectorMismatch = "security.house_selector_mismatch";
}
