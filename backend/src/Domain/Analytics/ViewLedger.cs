namespace ComicPlatform.Domain.Analytics;

/// <summary>Reporting days follow Vietnam time (UTC+7, no daylight saving), the payout calendar of the houses.</summary>
public static class ReportingDay
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateOnly From(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(Offset).DateTime);
}

/// <summary>
/// Verified views and earned revenue of one chapter on one reporting day — the payout ledger.
/// Comic and house ids are copied in (no foreign keys) so the history survives chapter/comic deletion.
/// Series totals are sums over their chapters.
/// </summary>
public sealed class ChapterViewDaily
{
    private ChapterViewDaily()
    {
    }

    public ChapterViewDaily(Guid chapterId, DateOnly day, Guid comicId, Guid translationHouseId, long views, long suspiciousViews, decimal revenue)
    {
        ChapterId = chapterId;
        Day = day;
        ComicId = comicId;
        TranslationHouseId = translationHouseId;
        Views = views;
        SuspiciousViews = suspiciousViews;
        Revenue = revenue;
    }

    public Guid ChapterId { get; private set; }
    public DateOnly Day { get; private set; }
    public Guid ComicId { get; private set; }
    public Guid TranslationHouseId { get; private set; }

    /// <summary>Views that passed every verification step (these are paid).</summary>
    public long Views { get; private set; }

    /// <summary>View attempts rejected as likely fake (bad/replayed tickets, too fast, bots, rate limits).</summary>
    public long SuspiciousViews { get; private set; }

    /// <summary>Revenue credited for <see cref="Views"/>, at the rate in force when each view was counted (VND).</summary>
    public decimal Revenue { get; private set; }
}

/// <summary>One counted view per (chapter, day, visitor): makes the daily de-duplication survive restarts.</summary>
public sealed class ViewReceipt
{
    private ViewReceipt()
    {
    }

    public ViewReceipt(Guid chapterId, DateOnly day, string visitorHash, DateTimeOffset createdAt)
    {
        ChapterId = chapterId;
        Day = day;
        VisitorHash = visitorHash;
        CreatedAt = createdAt;
    }

    public Guid ChapterId { get; private set; }
    public DateOnly Day { get; private set; }

    /// <summary>Keyed hash of the visitor (account, or network + browser); not reversible to an IP address.</summary>
    public string VisitorHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Rejected view attempts per day and reason (fraud monitoring for administrators).</summary>
public sealed class ViewRejectionDaily
{
    private ViewRejectionDaily()
    {
    }

    public ViewRejectionDaily(DateOnly day, string reason, long count)
    {
        Day = day;
        Reason = reason;
        Count = count;
    }

    public DateOnly Day { get; private set; }
    public string Reason { get; private set; } = null!;
    public long Count { get; private set; }
}
