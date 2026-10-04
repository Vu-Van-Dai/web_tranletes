namespace ComicPlatform.Domain.Identity;

/// <summary>
/// Opaque rotating refresh token. Only the SHA-256 hash is stored. Tokens rotated from the same
/// login share a <see cref="FamilyId"/>; presenting a revoked token revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string reason, Guid? replacedById = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokeReason = reason;
        ReplacedById = replacedById;
    }
}
