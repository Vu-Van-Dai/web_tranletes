using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Catalog;

public sealed class TagService(IAppDbContext db)
{
    public const int MaxTagsPerComic = 12;

    /// <summary>Finds or creates tags by display name (deduplicated by slug).</summary>
    public async Task<List<Tag>> EnsureTagsAsync(IReadOnlyList<string>? names, CancellationToken ct)
    {
        var cleaned = (names ?? [])
            .Select(TextNormalizer.CleanOptional)
            .Where(n => n is not null)
            .Select(n => n!)
            .DistinctBy(n => TextNormalizer.ToSlug(n, 60, "the-loai"))
            .ToList();

        var v = new Validator();
        v.Must(cleaned.Count <= MaxTagsPerComic, "tags", $"Tối đa {MaxTagsPerComic} thể loại cho mỗi truyện.");
        foreach (var name in cleaned)
        {
            v.Must(name.Length is >= 2 and <= 40, "tags", $"Tên thể loại \"{name}\" phải có từ 2 đến 40 ký tự.");
        }

        v.ThrowIfInvalid();

        var slugs = cleaned.Select(n => TextNormalizer.ToSlug(n, 60, "the-loai")).ToList();
        var existing = await db.Tags.Where(t => slugs.Contains(t.Slug)).ToListAsync(ct);
        var result = new List<Tag>();
        foreach (var name in cleaned)
        {
            var slug = TextNormalizer.ToSlug(name, 60, "the-loai");
            var tag = existing.FirstOrDefault(t => t.Slug == slug);
            if (tag is null)
            {
                tag = new Tag(name);
                db.Tags.Add(tag);
                existing.Add(tag);
            }

            result.Add(tag);
        }

        return result;
    }
}
