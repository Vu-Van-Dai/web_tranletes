using System.Globalization;
using System.Text;

namespace ComicPlatform.Domain.Common;

/// <summary>
/// Vietnamese-aware text helpers: diacritic removal (including đ/Đ), search keys and URL slugs.
/// </summary>
public static class TextNormalizer
{
    public static string RemoveDiacritics(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // đ/Đ are base letters (not combining marks), so FormD does not decompose them.
        var replaced = value.Replace('đ', 'd').Replace('Đ', 'D');
        var decomposed = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Lower-case, diacritic-free, whitespace-collapsed key used for trigram search.</summary>
    public static string ToSearchKey(params string?[] parts)
    {
        var joined = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        var plain = RemoveDiacritics(joined).ToLowerInvariant();
        var sb = new StringBuilder(plain.Length);
        var lastWasSpace = true;
        foreach (var ch in plain)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>ASCII slug: "Tôi Thăng Cấp Một Mình!" → "toi-thang-cap-mot-minh".</summary>
    public static string ToSlug(string? value, int maxLength = 80, string fallback = "item")
    {
        var plain = RemoveDiacritics(value).ToLowerInvariant();
        var sb = new StringBuilder(plain.Length);
        var lastWasDash = true;
        foreach (var ch in plain)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }

            if (sb.Length >= maxLength)
            {
                break;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>Trims and collapses internal whitespace; returns null for blank input.</summary>
    public static string? CleanOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
