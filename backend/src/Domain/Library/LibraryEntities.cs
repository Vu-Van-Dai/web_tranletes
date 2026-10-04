namespace ComicPlatform.Domain.Library;

/// <summary>A reader following ("theo dõi") a comic.</summary>
public sealed class Follow
{
    private Follow()
    {
    }

    public Follow(Guid userId, Guid comicId, DateTimeOffset now)
    {
        UserId = userId;
        ComicId = comicId;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public Guid ComicId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Last read position of a signed-in reader for one comic ("lịch sử đọc").</summary>
public sealed class ReadingProgress
{
    private ReadingProgress()
    {
    }

    public ReadingProgress(Guid userId, Guid comicId, Guid chapterId, int page, DateTimeOffset now)
    {
        UserId = userId;
        ComicId = comicId;
        ChapterId = chapterId;
        Page = page;
        UpdatedAt = now;
    }

    public Guid UserId { get; private set; }
    public Guid ComicId { get; private set; }
    public Guid ChapterId { get; private set; }
    public int Page { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(Guid chapterId, int page, DateTimeOffset now)
    {
        ChapterId = chapterId;
        Page = page;
        UpdatedAt = now;
    }
}

/// <summary>Aggregated daily view count per comic (written in batches).</summary>
public sealed class ComicViewDaily
{
    private ComicViewDaily()
    {
    }

    public ComicViewDaily(Guid comicId, DateOnly day, long views)
    {
        ComicId = comicId;
        Day = day;
        Views = views;
    }

    public Guid ComicId { get; private set; }
    public DateOnly Day { get; private set; }
    public long Views { get; private set; }
}
