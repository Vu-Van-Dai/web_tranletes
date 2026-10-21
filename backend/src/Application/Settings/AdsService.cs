using System.Text.Json;
using System.Text.RegularExpressions;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Settings;

/// <summary>
/// One advertisement. Formats:
/// <list type="bullet">
/// <item><c>banner</c> — inline slot(s) on public pages: image + link, or ad-network HTML (rendered in a sandboxed iframe).</item>
/// <item><c>popup</c> — overlay panel; closable at once (<c>button</c>) or only after a countdown (<c>countdown</c>).</item>
/// <item><c>newtab</c> — opens <c>TargetUrl</c> in a new tab on the visitor's first click (browsers block it without a click).</item>
/// <item><c>script</c> — ad-network code injected into public pages (popunder, social bar, auto ads…).</item>
/// </list>
/// Ads never render in the translation-house workspace or the admin area (enforced by the web app).
/// </summary>
public sealed record AdUnitDto(
    string Id,
    string Name,
    bool Enabled,
    string Format,
    string? ImageUrl,
    string? TargetUrl,
    string? Html,
    IReadOnlyList<string>? Slots,
    IReadOnlyList<string>? Pages,
    string? CloseMode,
    int? CountdownSeconds,
    int? DelaySeconds,
    int? FrequencyMinutes);

public sealed record AdsConfigDto(bool Enabled, IReadOnlyList<AdUnitDto> Units)
{
    public static readonly AdsConfigDto Empty = new(false, []);
}

public sealed partial class AdsService(IAppDbContext db, IMediaService media, IPublicUrls urls, StorageOutbox outbox, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxUnits = 50;
    public const int MaxHtmlLength = 20_000;

    public static readonly IReadOnlySet<string> Formats = new HashSet<string>(StringComparer.Ordinal) { "banner", "popup", "newtab", "script" };

    /// <summary>Inline banner positions rendered by the web app.</summary>
    public static readonly IReadOnlySet<string> Slots = new HashSet<string>(StringComparer.Ordinal)
    {
        "site_top", "site_bottom", "home_top", "home_middle", "browse_top", "comic_top", "comic_chapters", "house_top", "reader_top", "reader_bottom",
    };

    /// <summary>Public page groups a popup / new-tab / script ad can target (empty list = every public page).</summary>
    public static readonly IReadOnlySet<string> Pages = new HashSet<string>(StringComparer.Ordinal)
    {
        "home", "browse", "comic", "reader", "house", "library", "other",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AdsConfigDto> GetAsync(CancellationToken ct) => Parse(await ReadRawAsync(db, ct));

    /// <summary>What visitors receive: only enabled units, nothing at all when ads are switched off.</summary>
    public async Task<AdsConfigDto> GetPublicAsync(CancellationToken ct)
    {
        var config = await GetAsync(ct);
        return config.Enabled ? config with { Units = config.Units.Where(u => u.Enabled).ToList() } : AdsConfigDto.Empty;
    }

    public async Task<AdsConfigDto> UpdateAsync(AdsConfigDto request, CancellationToken ct)
    {
        var clean = Validate(request);
        var json = JsonSerializer.Serialize(clean, Json);
        var oldRaw = await ReadRawAsync(db, ct);
        var now = clock.GetUtcNow();
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SystemSetting.Ads, ct);
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting(SystemSetting.Ads, json, now, currentUser.UserId));
        }
        else
        {
            row.Set(json, now, currentUser.UserId);
        }

        // Uploaded creatives that are no longer used are withdrawn (the outbox re-checks references first).
        var stillUsed = AdImageKeys(json).ToHashSet(StringComparer.Ordinal);
        foreach (var key in AdImageKeys(oldRaw ?? "").Where(k => !stillUsed.Contains(k)).Distinct())
        {
            outbox.Withdraw(key, null);
        }

        audit.Add(AuditActions.AdsUpdated, "settings", null, null, new { clean.Enabled, units = clean.Units.Count });
        await db.SaveChangesAsync(ct);
        return clean;
    }

    /// <summary>Stores an ad creative as WebP and returns its public URL (to be referenced from the config).</summary>
    public async Task<string> UploadImageAsync(Stream image, CancellationToken ct)
    {
        var stored = await media.StoreAdImageAsync(image, ct);
        return urls.ObjectUrl(stored.Key)!;
    }

    /// <summary>True when the saved ads config references the object key (used before serving/deleting it).</summary>
    public static async Task<bool> ReferencesAsync(IAppDbContext db, string key, CancellationToken ct) =>
        (await ReadRawAsync(db, ct))?.Contains(key, StringComparison.Ordinal) == true;

    private static async Task<string?> ReadRawAsync(IAppDbContext db, CancellationToken ct) =>
        await db.SystemSettings.AsNoTracking().Where(s => s.Key == SystemSetting.Ads).Select(s => s.Value).FirstOrDefaultAsync(ct);

    private static AdsConfigDto Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return AdsConfigDto.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AdsConfigDto>(json, Json) ?? AdsConfigDto.Empty;
        }
        catch (JsonException)
        {
            return AdsConfigDto.Empty;
        }
    }

    private static IEnumerable<string> AdImageKeys(string json) => AdKeyRegex().Matches(json).Select(m => m.Value);

    public static AdsConfigDto Validate(AdsConfigDto request)
    {
        var v = new Validator();
        var units = request.Units ?? [];
        v.Must(units.Count <= MaxUnits, "units", $"Tối đa {MaxUnits} quảng cáo.");
        v.ThrowIfInvalid();

        var result = new List<AdUnitDto>(units.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < units.Count; i++)
        {
            var u = units[i];
            var f = $"units[{i}]";
            var format = u.Format?.Trim().ToLowerInvariant() ?? "";
            v.Must(u.Id is not null && IdRegex().IsMatch(u.Id) && ids.Add(u.Id), f + ".id", "Mã quảng cáo không hợp lệ hoặc bị trùng.");
            v.Required(f + ".name", u.Name, "Tên quảng cáo").Length(f + ".name", u.Name, "Tên quảng cáo", 1, 100);
            v.Must(Formats.Contains(format), f + ".format", "Loại quảng cáo không hợp lệ.");
            v.Url(f + ".imageUrl", u.ImageUrl, "Ảnh quảng cáo");
            v.Url(f + ".targetUrl", u.TargetUrl, "Liên kết đích");
            v.MaxLength(f + ".html", u.Html, "Mã quảng cáo", MaxHtmlLength);

            var hasImage = !string.IsNullOrWhiteSpace(u.ImageUrl) && !string.IsNullOrWhiteSpace(u.TargetUrl);
            var hasHtml = !string.IsNullOrWhiteSpace(u.Html);
            var slots = (u.Slots ?? []).Distinct().ToList();
            var pages = (u.Pages ?? []).Distinct().ToList();
            switch (format)
            {
                case "banner":
                    v.Must(hasImage || hasHtml, f + ".imageUrl", "Banner cần ảnh + liên kết đích, hoặc mã HTML của mạng quảng cáo.");
                    v.Must(slots.Count > 0 && slots.All(Slots.Contains), f + ".slots", "Hãy chọn ít nhất một vị trí hiển thị hợp lệ.");
                    break;
                case "popup":
                    v.Must(hasImage || hasHtml, f + ".imageUrl", "Pop-up cần ảnh + liên kết đích, hoặc mã HTML của mạng quảng cáo.");
                    v.Must(u.CloseMode is "button" or "countdown", f + ".closeMode", "Cách đóng pop-up không hợp lệ.");
                    v.Must(u.CloseMode != "countdown" || u.CountdownSeconds is >= 3 and <= 60, f + ".countdownSeconds", "Thời gian bắt buộc xem phải từ 3 đến 60 giây.");
                    break;
                case "newtab":
                    v.Must(!string.IsNullOrWhiteSpace(u.TargetUrl), f + ".targetUrl", "Hãy nhập liên kết sẽ mở trong tab mới.");
                    break;
                case "script":
                    v.Must(hasHtml, f + ".html", "Hãy dán mã quảng cáo của mạng quảng cáo.");
                    break;
            }

            v.Must(pages.All(Pages.Contains), f + ".pages", "Trang hiển thị không hợp lệ.");
            v.Must(u.DelaySeconds is null or >= 0 and <= 300, f + ".delaySeconds", "Độ trễ phải từ 0 đến 300 giây.");
            v.Must(u.FrequencyMinutes is null or >= 0 and <= 10_080, f + ".frequencyMinutes", "Tần suất phải từ 0 đến 10080 phút (7 ngày).");

            result.Add(new AdUnitDto(
                u.Id ?? "",
                u.Name?.Trim() ?? "",
                u.Enabled,
                format,
                Clean(u.ImageUrl),
                Clean(u.TargetUrl),
                string.IsNullOrWhiteSpace(u.Html) ? null : u.Html.Trim(),
                format == "banner" ? slots : [],
                format == "banner" ? [] : pages,
                format == "popup" ? u.CloseMode : null,
                format == "popup" && u.CloseMode == "countdown" ? u.CountdownSeconds : null,
                u.DelaySeconds,
                u.FrequencyMinutes));
        }

        v.ThrowIfInvalid();
        return new AdsConfigDto(request.Enabled, result);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"ads/[0-9a-f]{16}\.webp")]
    private static partial Regex AdKeyRegex();
}
