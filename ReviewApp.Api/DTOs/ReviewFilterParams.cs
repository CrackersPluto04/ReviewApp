namespace ReviewApp.Api.DTOs;

public record ReviewFilterParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;

    public string SortBy { get; init; } = "created_desc";
    public decimal MinScore { get; init; } = 1;
    public decimal MaxScore { get; init; } = 10;
    public bool HasWrittenText { get; init; } = false;
}
