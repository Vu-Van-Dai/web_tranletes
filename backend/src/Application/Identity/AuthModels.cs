using ComicPlatform.Domain.Tenancy;

namespace ComicPlatform.Application.Identity;

public sealed record RegisterRequest(string? Username, string? Email, string? DisplayName, string? Password);

public sealed record LoginRequest(string? Login, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record MembershipDto(
    Guid HouseId,
    string HouseName,
    string HouseSlug,
    string? LogoUrl,
    string HouseStatus,
    string Role,
    IReadOnlyList<string> Permissions);

public sealed record MeResponse(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string SystemRole,
    bool MustChangePassword,
    IReadOnlyList<MembershipDto> Memberships);

/// <summary>Result of login/refresh. The refresh token is written to an HttpOnly cookie, never the body.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    MeResponse User,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

public static class PermissionNames
{
    private static readonly (HousePermissions Flag, string Name)[] Map =
    [
        (HousePermissions.ViewMembers, "viewMembers"),
        (HousePermissions.ManageComics, "manageComics"),
        (HousePermissions.ManageChapters, "manageChapters"),
        (HousePermissions.UploadChapters, "uploadChapters"),
        (HousePermissions.PublishChapters, "publishChapters"),
        (HousePermissions.ManageMembers, "manageMembers"),
        (HousePermissions.ManageProfile, "manageProfile"),
    ];

    public static IReadOnlyList<string> ToNames(HousePermissions permissions) =>
        Map.Where(m => (permissions & m.Flag) == m.Flag).Select(m => m.Name).ToArray();

    /// <summary>Parses names into flags; unknown names are reported via <paramref name="unknown"/>.</summary>
    public static HousePermissions Parse(IEnumerable<string>? names, out IReadOnlyList<string> unknown)
    {
        var result = HousePermissions.None;
        var bad = new List<string>();
        foreach (var name in names ?? [])
        {
            var match = Map.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
            {
                bad.Add(name);
            }
            else
            {
                result |= match.Flag;
            }
        }

        unknown = bad;
        return result;
    }
}
