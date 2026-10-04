using System.Text.RegularExpressions;

namespace ComicPlatform.Application.Common;

/// <summary>
/// Tiny explicit validator producing Vietnamese field messages. Field keys are camelCase
/// to match the JSON payload the frontend sent.
/// </summary>
public sealed partial class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public Validator Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
        return this;
    }

    public Validator Required(string field, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"{label} không được để trống.");
        }

        return this;
    }

    public Validator Length(string field, string? value, string label, int min, int max)
    {
        if (value is null)
        {
            return this;
        }

        var len = value.Trim().Length;
        if (len < min || len > max)
        {
            Add(field, min > 0
                ? $"{label} phải có từ {min} đến {max} ký tự."
                : $"{label} không được vượt quá {max} ký tự.");
        }

        return this;
    }

    public Validator MaxLength(string field, string? value, string label, int max) => Length(field, value, label, 0, max);

    public Validator Email(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !EmailRegex().IsMatch(value.Trim()))
        {
            Add(field, "Email không hợp lệ.");
        }

        return this;
    }

    public Validator Username(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !UsernameRegex().IsMatch(value.Trim()))
        {
            Add(field, "Tên đăng nhập chỉ gồm chữ thường không dấu, số, dấu chấm hoặc gạch dưới (3–32 ký tự).");
        }

        return this;
    }

    public Validator Password(string field, string? value, string label = "Mật khẩu")
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(field, $"{label} không được để trống.");
        }
        else if (value.Length is < 10 or > 128)
        {
            Add(field, $"{label} phải có từ 10 đến 128 ký tự.");
        }

        return this;
    }

    public Validator Slug(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !SlugRegex().IsMatch(value))
        {
            Add(field, "Đường dẫn chỉ gồm chữ thường không dấu, số và dấu gạch ngang.");
        }

        return this;
    }

    public Validator Url(string field, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return this;
        }

        if (value.Length > 300 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            Add(field, $"{label} phải là đường dẫn http(s) hợp lệ.");
        }

        return this;
    }

    public Validator Range(string field, decimal value, string label, decimal min, decimal max)
    {
        if (value < min || value > max)
        {
            Add(field, $"{label} phải nằm trong khoảng {min} – {max}.");
        }

        return this;
    }

    public Validator Must(bool condition, string field, string message)
    {
        if (!condition)
        {
            Add(field, message);
        }

        return this;
    }

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw Errors.Validation(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal));
        }
    }

    [GeneratedRegex(@"^[^\s@]{1,64}@[^\s@]{1,255}\.[^\s@]{2,63}$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("^[a-z0-9_.]{3,32}$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
