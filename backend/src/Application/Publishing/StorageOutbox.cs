using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Publishing;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Publishing;

/// <summary>
/// Stages object-store side effects in the same transaction as the state change that requires them.
/// A background processor executes them with retries (and re-checks package state first).
/// </summary>
public sealed class StorageOutbox(IAppDbContext db, ICdnPurger purger, IPublicUrls urls, TimeProvider clock)
{
    public void Withdraw(string key, Guid? packageId)
    {
        var now = clock.GetUtcNow();
        db.StorageTasks.Add(new StorageTask(StorageTaskKind.DeleteObject, key, packageId, now));
        if (purger.IsEnabled && urls.ObjectUrl(key) is { } url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            db.StorageTasks.Add(new StorageTask(StorageTaskKind.PurgeUrl, url, packageId, now));
        }
    }
}

/// <summary>Keeps the denormalized comic counters used by the public catalog in sync.</summary>
public sealed class ComicStatsUpdater(IAppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Recomputes from the database plus pending (tracked, unsaved) chapter changes:
    /// call after mutating chapters and before SaveChanges.
    /// </summary>
    public async Task RecomputeAsync(Comic comic, CancellationToken ct)
    {
        var tracked = db.Chapters.Local.Where(c => c.ComicId == comic.Id).ToDictionary(c => c.Id);
        var stored = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => c.ComicId == comic.Id)
            .Select(c => new { c.Id, c.Status, c.PublishedAt })
            .ToListAsync(ct);

        var published = stored
            .Select(s => tracked.TryGetValue(s.Id, out var t) ? (t.Status, t.PublishedAt) : (s.Status, s.PublishedAt))
            .Concat(tracked.Values.Where(t => stored.All(s => s.Id != t.Id)).Select(t => (t.Status, t.PublishedAt)))
            .Where(x => x.Status == ChapterStatus.Published)
            .ToList();

        comic.UpdatePublishingStats(published.Count, published.Max(x => x.PublishedAt), clock.GetUtcNow());
    }
}
