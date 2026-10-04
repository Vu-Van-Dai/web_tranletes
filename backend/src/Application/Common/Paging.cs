namespace ComicPlatform.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public readonly record struct PageRequest(int Page, int PageSize)
{
    public static PageRequest Normalize(int? page, int? pageSize, int defaultSize = 24, int maxSize = 48)
    {
        var p = page is null or < 1 ? 1 : Math.Min(page.Value, 10_000);
        var s = pageSize is null or < 1 ? defaultSize : Math.Min(pageSize.Value, maxSize);
        return new PageRequest(p, s);
    }

    public int Skip => (Page - 1) * PageSize;
}
