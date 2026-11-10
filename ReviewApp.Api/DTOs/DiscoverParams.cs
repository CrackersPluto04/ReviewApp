namespace ReviewApp.Api.DTOs;

/// <summary>
/// Query-string filters of the TMDb discover endpoints (movies and series). Values are passed to TMDb as they are.
/// </summary>
public record TmdbParams
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;
    /// <summary>TMDb's <c>sort_by</c> value, e.g. <c>popularity.desc</c>.</summary>
    public string SortBy { get; init; } = "popularity.desc";
    /// <summary>Release year for movies, first air year for series.</summary>
    public int? Year { get; init; }
    /// <summary>TMDb genre IDs as TMDb's <c>with_genres</c> expects them.</summary>
    public string? WithGenres { get; init; }
    /// <summary>Runtime bounds in minutes.</summary>
    public int? MinRuntime { get; init; }
    public int? MaxRuntime { get; init; }
}

/// <summary>
/// Query-string filters of the Spotify discover endpoint. Spotify has no discover call, so they become a search query.
/// </summary>
public record SpotifyParams
{
    /// <summary>1-based page number; Spotify pages hold 10 tracks.</summary>
    public int Page { get; init; } = 1;
    public string Genre { get; init; } = "pop";
    /// <summary>Spotify's <c>year:</c> filter value.</summary>
    public string? Year { get; init; }
    /// <summary>Two-letter country code that decides which tracks are available.</summary>
    public string Market { get; init; } = "US";
}
