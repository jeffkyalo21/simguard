using Microsoft.EntityFrameworkCore;

namespace SimGuard.Api.Common;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);

public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // Values above MaxPageSize are clamped; values below 1 are rejected.
    public static PageRequest FromQuery(
        int? page,
        int? pageSize,
        ValidationErrors errors)
    {
        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
        {
            errors.Add("page", "page must be 1 or greater.");
        }

        if (resolvedPageSize < 1)
        {
            errors.Add("pageSize", "pageSize must be 1 or greater.");
        }

        return new PageRequest(
            resolvedPage,
            Math.Min(resolvedPageSize, MaxPageSize));
    }
}

public static class PagedQueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest paging,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);

        var skip = (long)(paging.Page - 1) * paging.PageSize;

        var items = skip >= total
            ? []
            : await query
                .Skip((int)skip)
                .Take(paging.PageSize)
                .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, paging.Page, paging.PageSize, total);
    }
}
