namespace ComicPlatform.Application.Common;

/// <summary>Conversions between domain values and their API representation.</summary>
public static class ApiText
{
    /// <summary>Enum → camelCase string ("PublishChapters" → "publishChapters").</summary>
    public static string Enum<T>(T value) where T : struct, System.Enum
    {
        var s = value.ToString();
        return s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }

    public static bool TryParseEnum<T>(string? value, out T result) where T : struct, System.Enum =>
        System.Enum.TryParse(value, ignoreCase: true, out result) && System.Enum.IsDefined(result);

    /// <summary>Strips trailing zeros from numeric(8,2) values: 12.00 → 12, 10.50 → 10.5.</summary>
    public static decimal ChapterNumber(decimal value) => value / 1.000000000000000000000000000000000m;
}
