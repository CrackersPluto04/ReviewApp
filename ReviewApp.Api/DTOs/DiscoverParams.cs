namespace ReviewApp.Api.DTOs;

public record TmdbParams
{
    public int Page { get; init; } = 1;
    public string SortBy { get; init; } = "popularity.desc";
    public int? Year { get; init; }
    public string? WithGenres { get; init; }
    public int? MinRuntime { get; init; }
    public int? MaxRuntime { get; init; }
}

public record SpotifyParams
{
    public int Page { get; init; } = 1;
    public string Genre { get; init; } = "pop";
    public string? Year { get; init; }
    public string Market { get; init; } = "US";
}
