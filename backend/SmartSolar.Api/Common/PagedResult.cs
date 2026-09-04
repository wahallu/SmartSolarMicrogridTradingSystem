/* =============================================================================
 * File        : PagedResult.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : Page of results plus the counters both clients need to render a
 *               pager. Paging is done in MongoDB with Skip/Limit so a large
 *               reservation history never has to travel to a phone in one lump.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Common;

/// <summary>One page of items together with its position in the whole set.</summary>
/// <typeparam name="T">Item type carried by the page.</typeparam>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public long TotalCount { get; set; }

    /// <summary>Total pages, computed so the clients never divide by zero.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    /// <summary>Wraps a materialised list into a page descriptor.</summary>
    public static PagedResult<T> Create(List<T> items, int page, int pageSize, long totalCount)
    {
        // Guards keep the page numbers sane even if a client sends page=0 or a negative size.
        return new PagedResult<T>
        {
            Items = items,
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 20 : pageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>Common paging inputs accepted by every list endpoint.</summary>
public class PagingQuery
{
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>One based page number; anything below 1 is clamped to 1.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Page size, clamped to the range 1..200 to protect the server.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Number of documents MongoDB must skip for the requested page.</summary>
    public int Skip => (Page - 1) * PageSize;
}
