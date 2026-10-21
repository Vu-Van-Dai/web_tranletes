using System.Globalization;
using System.Text.RegularExpressions;

namespace ComicPlatform.Application.Abstractions;

public enum ObjectKind
{
    Package,
    Cover,
    Logo,
    HouseCover,
    Ad,
}

public sealed record ParsedObjectKey(ObjectKind Kind, Guid OwnerId, Guid? ChapterId, int? Version, string? Hash)
{
    public string ContentType => Kind == ObjectKind.Package ? "application/octet-stream" : "image/webp";
}

/// <summary>
/// The only accepted object key shapes. Anything else (traversal, encoded slashes, upper-case GUIDs…)
/// is rejected, so keys can be safely mapped to file paths.
/// </summary>
public static partial class ObjectKeyParser
{
    public static bool TryParse(string? key, out ParsedObjectKey parsed)
    {
        parsed = null!;
        if (string.IsNullOrEmpty(key) || key.Length > 200)
        {
            return false;
        }

        var m = PackageRegex().Match(key);
        if (m.Success)
        {
            parsed = new ParsedObjectKey(ObjectKind.Package, Guid.Parse(m.Groups["comic"].Value), Guid.Parse(m.Groups["chapter"].Value),
                int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture), null);
            return parsed.Version is >= 1;
        }

        m = CoverRegex().Match(key);
        if (m.Success)
        {
            parsed = new ParsedObjectKey(ObjectKind.Cover, Guid.Parse(m.Groups["comic"].Value), null, null, m.Groups["hash"].Value);
            return true;
        }

        m = LogoRegex().Match(key);
        if (m.Success)
        {
            parsed = new ParsedObjectKey(ObjectKind.Logo, Guid.Parse(m.Groups["house"].Value), null, null, m.Groups["hash"].Value);
            return true;
        }

        m = HouseCoverRegex().Match(key);
        if (m.Success)
        {
            parsed = new ParsedObjectKey(ObjectKind.HouseCover, Guid.Parse(m.Groups["house"].Value), null, null, m.Groups["hash"].Value);
            return true;
        }

        m = AdRegex().Match(key);
        if (m.Success)
        {
            parsed = new ParsedObjectKey(ObjectKind.Ad, Guid.Empty, null, null, m.Groups["hash"].Value);
            return true;
        }

        return false;
    }

    private const string GuidPattern = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

    [GeneratedRegex("^comics/(?<comic>" + GuidPattern + ")/chapters/(?<chapter>" + GuidPattern + @")/v(?<v>[1-9][0-9]{0,5})\.pkg$")]
    private static partial Regex PackageRegex();

    [GeneratedRegex("^comics/(?<comic>" + GuidPattern + @")/cover/(?<hash>[0-9a-f]{16})\.webp$")]
    private static partial Regex CoverRegex();

    [GeneratedRegex("^houses/(?<house>" + GuidPattern + @")/logo/(?<hash>[0-9a-f]{16})\.webp$")]
    private static partial Regex LogoRegex();

    [GeneratedRegex("^houses/(?<house>" + GuidPattern + @")/cover/(?<hash>[0-9a-f]{16})\.webp$")]
    private static partial Regex HouseCoverRegex();

    [GeneratedRegex(@"^ads/(?<hash>[0-9a-f]{16})\.webp$")]
    private static partial Regex AdRegex();
}
