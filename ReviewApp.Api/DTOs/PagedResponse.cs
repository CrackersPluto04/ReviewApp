namespace ReviewApp.Api.DTOs;

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }

    public PagedResponse(IEnumerable<T> items, int totalCount, int currentPage, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        CurrentPage = currentPage;
        // No valid page size (e.g. an empty search/all response) means no pages, not (int)NaN
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
    }
}
