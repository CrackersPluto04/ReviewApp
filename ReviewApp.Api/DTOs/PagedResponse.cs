namespace ReviewApp.Api.DTOs;

/// <summary>
/// A page of results with the numbers a client needs to build a pager.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; }
    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; set; }
    /// <summary>The 1-based number of this page.</summary>
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }

    /// <param name="items">The items of this page.</param>
    /// <param name="totalCount">Total number of items across all pages.</param>
    /// <param name="currentPage">The 1-based number of this page.</param>
    /// <param name="pageSize">Items per page, used only to derive <see cref="TotalPages"/>.</param>
    public PagedResponse(IEnumerable<T> items, int totalCount, int currentPage, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        CurrentPage = currentPage;
        // No valid page size (e.g. an empty search/all response) means no pages, not (int)NaN
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
    }
}
