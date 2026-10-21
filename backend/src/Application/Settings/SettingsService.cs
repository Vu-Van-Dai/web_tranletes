using System.Text.Json;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Settings;

/// <param name="RevenuePerThousandViews">VND credited to a chapter's house per 1,000 verified views (applies to views counted from now on).</param>
public sealed record SystemSettingsDto(bool RegistrationEnabled, string? Announcement, decimal RevenuePerThousandViews);

public sealed record UpdateSystemSettingsRequest(bool? RegistrationEnabled, string? Announcement, decimal? RevenuePerThousandViews = null);

public sealed class SettingsService(IAppDbContext db, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<SystemSettingsDto> GetAsync(CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new SystemSettingsDto(
            ReadBool(rows, SystemSetting.RegistrationEnabled, true),
            ReadString(rows, SystemSetting.Announcement),
            ReadDecimal(rows, SystemSetting.RevenuePerThousandViews));
    }

    public async Task<SystemSettingsDto> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken ct)
    {
        var v = new Validator();
        v.MaxLength("announcement", request.Announcement, "Thông báo", 500);
        if (request.RevenuePerThousandViews is { } rate)
        {
            v.Range("revenuePerThousandViews", rate, "Đơn giá / 1.000 lượt xem", 0, 100_000_000);
        }
        v.ThrowIfInvalid();

        var now = clock.GetUtcNow();
        if (request.RegistrationEnabled is { } enabled)
        {
            await UpsertAsync(SystemSetting.RegistrationEnabled, JsonSerializer.Serialize(enabled), now, ct);
        }

        if (request.Announcement is not null)
        {
            var text = request.Announcement.Trim();
            await UpsertAsync(SystemSetting.Announcement, JsonSerializer.Serialize(text.Length == 0 ? null : text), now, ct);
        }

        if (request.RevenuePerThousandViews is { } newRate)
        {
            await UpsertAsync(SystemSetting.RevenuePerThousandViews, JsonSerializer.Serialize(decimal.Round(newRate, 2)), now, ct);
        }

        audit.Add(AuditActions.SettingsUpdated, "settings", null, null, request);
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private async Task UpsertAsync(string key, string json, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting(key, json, now, currentUser.UserId));
        }
        else
        {
            row.Set(json, now, currentUser.UserId);
        }
    }

    private static bool ReadBool(Dictionary<string, string> rows, string key, bool fallback) =>
        rows.TryGetValue(key, out var json) && JsonSerializer.Deserialize<bool?>(json) is { } b ? b : fallback;

    private static decimal ReadDecimal(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var json) && JsonSerializer.Deserialize<decimal?>(json) is { } d ? d : 0;

    private static string? ReadString(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<string?>(json) : null;
}
