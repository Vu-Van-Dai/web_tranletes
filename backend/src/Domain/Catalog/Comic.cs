using ComicPlatform.Domain.Common;
using ComicPlatform.Domain.Tenancy;

namespace ComicPlatform.Domain.Catalog;

public enum ComicStatus
{
    Ongoing = 0,
    Completed = 1,
    Hiatus = 2,
    Dropped = 3,
}

public sealed class Comic
{
    private readonly List<ComicTag> _tags = [];

    private Comic()
    {
    }

    public Comic(Guid houseId, ComicDetails details, string slug, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TranslationHouseId = houseId;
        CreatedAt = now;
        Apply(details, slug, now);
    }

    public Guid Id { get; private set; }
    public Guid TranslationHouseId { get; private set; }
    public string OriginalTitle { get; private set; } = null!;
    public string TranslatedTitle { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Author { get; private set; }
    public string? Artist { get; private set; }
    public string? CoverKey { get; private set; }
    public ComicStatus Status { get; private set; }
    public bool IsMature { get; private set; }
    public string SearchKey { get; private set; } = null!;
    public int PublishedChapterCount { get; private set; }
    public DateTimeOffset? LastChapterPublishedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint Version { get; private set; }

    public TranslationHouse? TranslationHouse { get; private set; }
    public IReadOnlyCollection<ComicTag> Tags => _tags;

    public void Update(ComicDetails details, string slug, DateTimeOffset now) => Apply(details, slug, now);

    public void SetTags(IEnumerable<Tag> tags)
    {
        var wanted = tags.DistinctBy(t => t.Id).ToDictionary(t => t.Id);
        _tags.RemoveAll(ct => !wanted.ContainsKey(ct.TagId));
        foreach (var tag in wanted.Values)
        {
            if (_tags.All(ct => ct.TagId != tag.Id))
            {
                _tags.Add(new ComicTag(Id, tag.Id));
            }
        }
    }

    public void SetCover(string? coverKey, DateTimeOffset now)
    {
        CoverKey = coverKey;
        UpdatedAt = now;
    }

    public void UpdatePublishingStats(int publishedChapterCount, DateTimeOffset? lastPublishedAt, DateTimeOffset now)
    {
        PublishedChapterCount = publishedChapterCount;
        LastChapterPublishedAt = lastPublishedAt;
        UpdatedAt = now;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        DeletedAt = now;
        PublishedChapterCount = 0;
        UpdatedAt = now;
    }

    private void Apply(ComicDetails d, string slug, DateTimeOffset now)
    {
        TranslatedTitle = d.TranslatedTitle.Trim();
        OriginalTitle = d.OriginalTitle.Trim();
        Description = d.Description?.Trim();
        Author = TextNormalizer.CleanOptional(d.Author);
        Artist = TextNormalizer.CleanOptional(d.Artist);
        Status = d.Status;
        IsMature = d.IsMature;
        Slug = slug;
        SearchKey = TextNormalizer.ToSearchKey(TranslatedTitle, OriginalTitle, Author, Artist);
        UpdatedAt = now;
    }
}

public sealed record ComicDetails(
    string OriginalTitle,
    string TranslatedTitle,
    string? Description,
    string? Author,
    string? Artist,
    ComicStatus Status,
    bool IsMature);

public sealed class Tag
{
    private Tag()
    {
    }

    public Tag(string name)
    {
        Id = Guid.CreateVersion7();
        Name = name.Trim();
        Slug = TextNormalizer.ToSlug(name, 60, "the-loai");
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
}

public sealed class ComicTag
{
    private ComicTag()
    {
    }

    public ComicTag(Guid comicId, Guid tagId)
    {
        ComicId = comicId;
        TagId = tagId;
    }

    public Guid ComicId { get; private set; }
    public Guid TagId { get; private set; }
    public Tag? Tag { get; private set; }
}
