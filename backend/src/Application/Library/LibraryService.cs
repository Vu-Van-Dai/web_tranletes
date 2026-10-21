using System.Collections.Concurrent;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Library;

public sealed record SaveProgressRequest(Guid? ChapterId, int? Page);

public sealed record HistoryItem(ComicCardDto Comic, ChapterRef Chapter, int Page, DateTimeOffset UpdatedAt);

public sealed record ComicLibraryState(bool Following, ChapterRef? LastRead, int? LastPage);

/// <summary>Per-reader features: follows ("theo dõi"), reading history and rankings.</summary>
public sealed class LibraryService(IAppDbContext db, PublicCatalogService catalog, TimeProvider clock)
{
    public async Task FollowAsync(Guid userId, Guid comicId, CancellationToken ct)
    {
        if (!await catalog.IsComicVisibleAsync(comicId, ct))
        {
            throw Errors.NotFound("Truyện");
        }

        if (!await db.Follows.AnyAsync(f => f.UserId == userId && f.ComicId == comicId, ct))
        {
            db.Follows.Add(new Follow(userId, comicId, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UnfollowAsync(Guid userId, Guid comicId, CancellationToken ct)
    {
        await db.Follows.Where(f => f.UserId == userId && f.ComicId == comicId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<ComicCardDto>> FollowsAsync(Guid userId, CancellationToken ct)
    {
        var ids = await db.Follows.AsNoTracking().Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt).Select(f => f.ComicId).Take(500).ToListAsync(ct);
        var cards = await catalog.CardsByIdsAsync(ids, ct);
        return cards.OrderByDescending(c => c.LastChapterPublishedAt).ToList();
    }

    public async Task<ComicLibraryState> StateAsync(Guid userId, Guid comicId, CancellationToken ct)
    {
        var following = await db.Follows.AnyAsync(f => f.UserId == userId && f.ComicId == comicId, ct);
        var progress = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId && p.ComicId == comicId)
            .Select(p => new { p.ChapterId, p.Page }).FirstOrDefaultAsync(ct);
        ChapterRef? last = null;
        if (progress is not null)
        {
            var ch = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
                .Where(c => c.Id == progress.ChapterId && c.Status == Domain.Catalog.ChapterStatus.Published)
                .Select(c => new { c.Id, c.ChapterNumber, c.Title, c.PublishedAt }).FirstOrDefaultAsync(ct);
            if (ch is not null)
            {
                last = new ChapterRef(ch.Id, ApiText.ChapterNumber(ch.ChapterNumber), ch.Title, ch.PublishedAt);
            }
        }

        return new ComicLibraryState(following, last, last is null ? null : progress!.Page);
    }

    public async Task SaveProgressAsync(Guid userId, SaveProgressRequest request, CancellationToken ct)
    {
        if (request.ChapterId is null)
        {
            throw Errors.Field("chapterId", "Thiếu chương đang đọc.");
        }

        var comicId = (await catalog.VisibleChapterInfoAsync(request.ChapterId.Value, ct) ?? throw Errors.NotFound("Chương")).ComicId;
        var page = Math.Clamp(request.Page ?? 1, 1, 10_000);
        var now = clock.GetUtcNow();
        var existing = await db.ReadingProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.ComicId == comicId, ct);
        if (existing is null)
        {
            db.ReadingProgress.Add(new ReadingProgress(userId, comicId, request.ChapterId.Value, page, now));
        }
        else
        {
            existing.Update(request.ChapterId.Value, page, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two tabs racing on the first insert: last write wins next time; nothing to report.
        }
    }

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt).Take(100)
            .Select(p => new { p.ComicId, p.ChapterId, p.Page, p.UpdatedAt })
            .ToListAsync(ct);
        var cards = (await catalog.CardsByIdsAsync(rows.Select(r => r.ComicId).ToList(), ct)).ToDictionary(c => c.Id);
        var chapterIds = rows.Select(r => r.ChapterId).ToList();
        var chapters = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(c => chapterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ChapterNumber, c.Title, c.PublishedAt })
            .ToDictionaryAsync(c => c.Id, ct);

        return rows.Where(r => cards.ContainsKey(r.ComicId) && chapters.ContainsKey(r.ChapterId))
            .Select(r =>
            {
                var ch = chapters[r.ChapterId];
                return new HistoryItem(cards[r.ComicId], new ChapterRef(ch.Id, ApiText.ChapterNumber(ch.ChapterNumber), ch.Title, ch.PublishedAt), r.Page, r.UpdatedAt);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ComicCardDto>> RankingsAsync(string? period, CancellationToken ct)
    {
        var days = period switch
        {
            "week" => 7,
            "month" => 30,
            _ => 1,
        };
        var since = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-(days - 1));
        var ids = await db.ComicViewDaily.AsNoTracking()
            .Where(v => v.Day >= since)
            .GroupBy(v => v.ComicId)
            .Select(g => new { ComicId = g.Key, Views = g.Sum(x => x.Views) })
            .OrderByDescending(x => x.Views)
            .Take(20)
            .Select(x => x.ComicId)
            .ToListAsync(ct);
        return (await catalog.CardsByIdsAsync(ids, ct)).Take(10).ToList();
    }
}
