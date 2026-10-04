namespace ComicPlatform.Domain.Auditing;

public sealed class AuditLog
{
    private AuditLog()
    {
    }

    public AuditLog(DateTimeOffset at, Guid? actorUserId, string action, string? targetType, string? targetId, Guid? houseId, string? ip, string? dataJson, bool success)
    {
        Id = Guid.CreateVersion7();
        At = at;
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        TranslationHouseId = houseId;
        Ip = ip;
        Data = dataJson;
        Success = success;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public Guid? TranslationHouseId { get; private set; }
    public string? Ip { get; private set; }
    public string? Data { get; private set; }
    public bool Success { get; private set; }
}
