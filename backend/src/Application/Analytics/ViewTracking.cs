using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Analytics;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Analytics;

public sealed class ViewOptions
{
    /// <summary>Stable server secret (derived from the JWT key) used for visitor hashes and ticket keys.</summary>
    public byte[] Secret { get; init; } = [];

    /// <summary>Minimum reading time per page; the required time is clamped to [MinSeconds, MaxSeconds].</summary>
    public double SecondsPerPage { get; init; } = 1.5;

    public int MinSeconds { get; init; } = 10;
    public int MaxSeconds { get; init; } = 60;

    /// <summary>Share of the pages the reader must have displayed.</summary>
    public double MinPageFraction { get; init; } = 0.5;

    public TimeSpan TicketLifetime { get; init; } = TimeSpan.FromHours(6);
    public int MaxViewsPerVisitorPerHour { get; init; } = 60;
    public int MaxViewsPerIpPerHour { get; init; } = 300;
}

/// <summary>Who is reading: the account (if signed in) plus network and browser.</summary>
public sealed record ViewVisitor(Guid? UserId, string Ip, string? UserAgent);

public sealed record ViewTicketDto(string Ticket, int MinSeconds, int MinPages);

public sealed record ViewReportRequest(string? Ticket, int PagesSeen, int ActiveSeconds);

/// <summary>Optional human check (Cloudflare Turnstile) required before a view ticket is issued.</summary>
public interface IHumanVerifier
{
    bool IsEnabled { get; }
    Task<bool> VerifyAsync(string? token, string ip, CancellationToken ct);
}

public sealed class NoHumanVerifier : IHumanVerifier
{
    public bool IsEnabled => false;

    public Task<bool> VerifyAsync(string? token, string ip, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>Why a view was not counted. Suspicious reasons are reported per chapter as likely fake views.</summary>
public static class ViewRejection
{
    public const string NoTicket = "no_ticket";
    public const string InvalidTicket = "invalid_ticket";
    public const string TooFast = "too_fast";
    public const string Expired = "expired";
    public const string VisitorMismatch = "visitor_mismatch";
    public const string Bot = "bot";
    public const string Replay = "replay";
    public const string Incomplete = "incomplete";
    public const string OwnHouse = "own_house";
    public const string RateLimited = "rate_limited";
    public const string HumanCheckFailed = "human_check_failed";

    /// <summary>Signals of automation or tampering (as opposed to a real reader who simply left early).</summary>
    public static readonly IReadOnlySet<string> Suspicious = new HashSet<string>(StringComparer.Ordinal)
    {
        NoTicket, InvalidTicket, TooFast, VisitorMismatch, Bot, Replay, RateLimited, HumanCheckFailed,
    };
}

/// <summary>
/// Signed, single-use view tickets. A ticket is issued when a reader opens a chapter and binds the chapter,
/// the issue time, the required reading time and the visitor (account or network + browser). The server
/// only counts the view when the ticket comes back after that reading time — the client cannot shorten it,
/// forge a ticket, reuse it, or hand it to another machine. The ticket key changes on every restart, so the
/// in-memory replay table is sufficient.
/// </summary>
public sealed partial class ViewTicketService
{
    private const byte FormatVersion = 1;
    private const int PayloadLength = 1 + 16 + 8 + 2 + 2 + 16 + 16;
    private const int MacLength = 32;
    private const int MaxReplayEntries = 500_000;

    private readonly ViewOptions _options;
    private readonly byte[] _ticketKey;
    private readonly byte[] _visitorKey;
    private readonly ConcurrentDictionary<Guid, long> _usedNonces = new();

    public ViewTicketService(ViewOptions options)
    {
        _options = options;
        var boot = RandomNumberGenerator.GetBytes(32);
        _ticketKey = HMACSHA256.HashData(options.Secret, (byte[])[.. "view-ticket|"u8.ToArray(), .. boot]);
        _visitorKey = HMACSHA256.HashData(options.Secret, "view-visitor"u8.ToArray());
    }

    public int RequiredSeconds(int pageCount) =>
        Math.Clamp((int)Math.Ceiling(pageCount * _options.SecondsPerPage), _options.MinSeconds, _options.MaxSeconds);

    public int RequiredPages(int pageCount) => Math.Max(1, (int)Math.Ceiling(pageCount * _options.MinPageFraction));

    public ViewTicketDto Issue(Guid chapterId, int pageCount, ViewVisitor visitor, DateTimeOffset now)
    {
        var minSeconds = RequiredSeconds(pageCount);
        var minPages = RequiredPages(pageCount);
        var buffer = new byte[PayloadLength + MacLength];
        var payload = buffer.AsSpan(0, PayloadLength);
        payload[0] = FormatVersion;
        chapterId.TryWriteBytes(payload[1..17]);
        BinaryPrimitives.WriteInt64LittleEndian(payload[17..25], now.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt16LittleEndian(payload[25..27], (ushort)minSeconds);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[27..29], (ushort)minPages);
        BindingHash(visitor).CopyTo(payload[29..45]);
        RandomNumberGenerator.Fill(payload[45..61]);
        HMACSHA256.HashData(_ticketKey, payload, buffer.AsSpan(PayloadLength));
        return new ViewTicketDto(Base64Url.Encode(buffer), minSeconds, minPages);
    }

    /// <summary>Returns null when the view may be counted, otherwise the <see cref="ViewRejection"/> reason.</summary>
    public string? Check(Guid chapterId, ViewReportRequest report, ViewVisitor visitor, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(report.Ticket))
        {
            return ViewRejection.NoTicket;
        }

        if (IsBot(visitor.UserAgent))
        {
            return ViewRejection.Bot;
        }

        var bytes = Base64Url.TryDecode(report.Ticket);
        if (bytes is null || bytes.Length != PayloadLength + MacLength || bytes[0] != FormatVersion)
        {
            return ViewRejection.InvalidTicket;
        }

        var payload = bytes.AsSpan(0, PayloadLength);
        Span<byte> mac = stackalloc byte[MacLength];
        HMACSHA256.HashData(_ticketKey, payload, mac);
        if (!CryptographicOperations.FixedTimeEquals(mac, bytes.AsSpan(PayloadLength)) || new Guid(payload[1..17]) != chapterId)
        {
            return ViewRejection.InvalidTicket;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(payload[17..25]));
        var minSeconds = BinaryPrimitives.ReadUInt16LittleEndian(payload[25..27]);
        var minPages = BinaryPrimitives.ReadUInt16LittleEndian(payload[27..29]);
        var age = now - issuedAt;
        if (age < TimeSpan.FromSeconds(minSeconds))
        {
            return ViewRejection.TooFast;
        }

        if (age > _options.TicketLifetime)
        {
            return ViewRejection.Expired;
        }

        if (!CryptographicOperations.FixedTimeEquals(payload[29..45], BindingHash(visitor)))
        {
            return ViewRejection.VisitorMismatch;
        }

        if (report.PagesSeen < minPages || report.ActiveSeconds < minSeconds)
        {
            return ViewRejection.Incomplete;
        }

        var nonce = new Guid(payload[45..61]);
        var expires = (issuedAt + _options.TicketLifetime).ToUnixTimeSeconds();
        if (_usedNonces.Count >= MaxReplayEntries)
        {
            PruneNonces(now);
        }

        return _usedNonces.TryAdd(nonce, expires) ? null : ViewRejection.Replay;
    }

    /// <summary>Daily de-duplication key of a visitor (keyed hash; a new salt every day).</summary>
    public string VisitorHash(ViewVisitor visitor, DateOnly day)
    {
        var identity = visitor.UserId is { } id ? "u:" + id.ToString("N") : "a:" + NetworkPrefix(visitor.Ip) + "|" + (visitor.UserAgent ?? "");
        var hash = HMACSHA256.HashData(_visitorKey, Encoding.UTF8.GetBytes(day.DayNumber + "|" + identity));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    public static string NetworkKey(string ip) => NetworkPrefix(ip);

    public void PruneNonces(DateTimeOffset now)
    {
        var cutoff = now.ToUnixTimeSeconds();
        foreach (var (nonce, expires) in _usedNonces)
        {
            if (expires < cutoff)
            {
                _usedNonces.TryRemove(nonce, out _);
            }
        }
    }

    public static bool IsBot(string? userAgent) => string.IsNullOrWhiteSpace(userAgent) || userAgent.Length < 20 || BotRegex().IsMatch(userAgent);

    /// <summary>Ticket binding: account id, or network prefix (/24, /48) + browser for anonymous readers.</summary>
    private byte[] BindingHash(ViewVisitor visitor)
    {
        var identity = visitor.UserId is { } id ? "u:" + id.ToString("N") : "a:" + NetworkPrefix(visitor.Ip) + "|" + (visitor.UserAgent ?? "");
        return HMACSHA256.HashData(_ticketKey, Encoding.UTF8.GetBytes(identity))[..16];
    }

    /// <summary>/24 for IPv4 and /48 for IPv6, so a phone hopping between nearby addresses keeps its ticket.</summary>
    private static string NetworkPrefix(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return ip;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24"
            : Convert.ToHexString(bytes.AsSpan(0, 6)) + "/48";
    }

    [GeneratedRegex(@"bot|crawl|spider|slurp|headless|phantom|selenium|puppeteer|playwright|python|curl|wget|httpclient|okhttp|java/|go-http|axios|node-fetch|libwww|scrapy|facebookexternalhit|preview", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();
}

/// <summary>
/// In-memory aggregation of verified and rejected views (no per-view DB writes); a background service
/// flushes it every minute. Daily de-duplication is enforced in the database (view_receipts).
/// </summary>
public sealed class ViewCounter
{
    private const int MaxPending = 500_000;
    private const int MaxRateEntries = 500_000;

    private ConcurrentQueue<AcceptedView> _accepted = new();
    private int _pending;
    private ConcurrentDictionary<(Guid ChapterId, Guid ComicId, Guid HouseId, DateOnly Day), long> _suspicious = new();
    private ConcurrentDictionary<(DateOnly Day, string Reason), long> _rejections = new();
    private readonly ConcurrentDictionary<string, int> _hourly = new(StringComparer.Ordinal);

    public void Accept(AcceptedView view)
    {
        if (Interlocked.Increment(ref _pending) > MaxPending)
        {
            Interlocked.Decrement(ref _pending);
            return;
        }

        _accepted.Enqueue(view);
    }

    public void Reject(VisibleChapterInfo? chapter, DateOnly day, string reason)
    {
        _rejections.AddOrUpdate((day, reason), 1, (_, v) => v + 1);
        if (chapter is not null && ViewRejection.Suspicious.Contains(reason))
        {
            _suspicious.AddOrUpdate((chapter.ChapterId, chapter.ComicId, chapter.HouseId, day), 1, (_, v) => v + 1);
        }
    }

    /// <summary>Consumes one slot of the visitor's and the network's hourly budgets (fixed one-hour windows).</summary>
    public bool TryConsume(string visitorKey, string networkKey, DateTimeOffset now, int perVisitor, int perNetwork)
    {
        var hour = now.ToUnixTimeSeconds() / 3600;
        var v = $"{hour}|v|{visitorKey}";
        var n = $"{hour}|n|{networkKey}";
        if (_hourly.Count >= MaxRateEntries)
        {
            PruneRates(now);
        }

        if (_hourly.GetValueOrDefault(v) >= perVisitor || _hourly.GetValueOrDefault(n) >= perNetwork)
        {
            return false;
        }

        _hourly.AddOrUpdate(v, 1, (_, c) => c + 1);
        _hourly.AddOrUpdate(n, 1, (_, c) => c + 1);
        return true;
    }

    public ViewBatch Drain(DateTimeOffset now)
    {
        var accepted = Interlocked.Exchange(ref _accepted, new ConcurrentQueue<AcceptedView>());
        Interlocked.Exchange(ref _pending, 0);
        var suspicious = Interlocked.Exchange(ref _suspicious, new());
        var rejections = Interlocked.Exchange(ref _rejections, new());
        PruneRates(now);
        return new ViewBatch(accepted.ToArray(), suspicious.ToArray(), rejections.ToArray());
    }

    private void PruneRates(DateTimeOffset now)
    {
        var prefix = (now.ToUnixTimeSeconds() / 3600).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|";
        foreach (var key in _hourly.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _hourly.TryRemove(key, out _);
            }
        }
    }
}

public sealed record AcceptedView(Guid ChapterId, Guid ComicId, Guid HouseId, DateOnly Day, string VisitorHash);

public sealed record ViewBatch(
    AcceptedView[] Accepted,
    KeyValuePair<(Guid ChapterId, Guid ComicId, Guid HouseId, DateOnly Day), long>[] Suspicious,
    KeyValuePair<(DateOnly Day, string Reason), long>[] Rejections)
{
    public bool IsEmpty => Accepted.Length == 0 && Suspicious.Length == 0 && Rejections.Length == 0;
}

/// <summary>Issues view tickets and decides whether a reported view is counted.</summary>
public sealed class ViewService(
    IAppDbContext db,
    PublicCatalogService catalog,
    ViewTicketService tickets,
    ViewCounter counter,
    IHumanVerifier human,
    ViewOptions options,
    TimeProvider clock,
    ILogger<ViewService> logger)
{
    public async Task<ViewTicketDto> IssueTicketAsync(Guid chapterId, string? humanToken, ViewVisitor visitor, CancellationToken ct)
    {
        var chapter = await catalog.VisibleChapterInfoAsync(chapterId, ct) ?? throw Errors.NotFound("Chương");
        var now = clock.GetUtcNow();
        if (human.IsEnabled && !await human.VerifyAsync(humanToken, visitor.Ip, ct))
        {
            counter.Reject(chapter, ReportingDay.From(now), ViewRejection.HumanCheckFailed);
            throw Errors.Forbidden("human_check_failed", "Không xác minh được trình duyệt. Vui lòng tải lại trang.");
        }

        return tickets.Issue(chapterId, chapter.PageCount, visitor, now);
    }

    /// <summary>Counts the view only if every check passes; never tells the caller which check failed.</summary>
    public async Task RecordAsync(Guid chapterId, ViewReportRequest report, ViewVisitor visitor, CancellationToken ct)
    {
        var chapter = await catalog.VisibleChapterInfoAsync(chapterId, ct) ?? throw Errors.NotFound("Chương");
        var now = clock.GetUtcNow();
        var day = ReportingDay.From(now);
        var reason = tickets.Check(chapterId, report, visitor, now);

        // A house's own members reading its chapters are not paid views.
        if (reason is null && visitor.UserId is { } userId
            && await db.TranslationHouseMembers.AnyAsync(m => m.UserId == userId && m.TranslationHouseId == chapter.HouseId && m.Status == MemberStatus.Active, ct))
        {
            reason = ViewRejection.OwnHouse;
        }

        var visitorHash = tickets.VisitorHash(visitor, day);
        if (reason is null && !counter.TryConsume(visitorHash, ViewTicketService.NetworkKey(visitor.Ip), now, options.MaxViewsPerVisitorPerHour, options.MaxViewsPerIpPerHour))
        {
            reason = ViewRejection.RateLimited;
        }

        if (reason is null)
        {
            counter.Accept(new AcceptedView(chapter.ChapterId, chapter.ComicId, chapter.HouseId, day, visitorHash));
        }
        else
        {
            counter.Reject(chapter, day, reason);
            if (ViewRejection.Suspicious.Contains(reason))
            {
                logger.LogInformation("View rejected for chapter {ChapterId}: {Reason}", chapterId, reason);
            }
        }
    }
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? TryDecode(string text)
    {
        if (text.Length > 200)
        {
            return null;
        }

        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        var buffer = new byte[s.Length];
        return Convert.TryFromBase64String(s, buffer, out var written) ? buffer[..written] : null;
    }
}
