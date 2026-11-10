namespace ReviewApp.Api.DTOs;

/// <summary>
/// Query-string filter, sort and paging parameters shared by the review lists (per title and per user).
/// </summary>
public record ReviewFilterParams
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;

    /// <summary>One of <c>created_desc</c> (default), <c>created_asc</c>, <c>updated_desc</c>, <c>updated_asc</c>, <c>score_desc</c>, <c>score_asc</c>.</summary>
    public string SortBy { get; init; } = "created_desc";
    /// <summary>Lower bound of the score range; the defaults span the whole 1 to 10 scale.</summary>
    public decimal MinScore { get; init; } = 1;
    public decimal MaxScore { get; init; } = 10;
    /// <summary>Only reviews that have written text, not just a score.</summary>
    public bool HasWrittenText { get; init; } = false;
}
