using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

// Mirrors of the parts of TMDb's JSON that the app reads. Used only to deserialize TMDb's responses and never
// returned to clients; property names are mapped to TMDb's snake_case.

/// <summary>
/// TMDb's paged search / discover response for movies or series.
/// </summary>
public class TmdbSearchResponseDto
{
    [JsonPropertyName("results")]
    public List<TmdbItemDto> Results { get; set; } = [];

    /// <summary>Total matches across all pages.</summary>
    [JsonPropertyName("total_results")]
    public int TotalCount { get; set; } = 0;
}

/// <summary>
/// A movie or series. TMDb names some fields differently for each kind, so both variants are mapped and the
/// <c>Display*</c> properties pick whichever is present.
/// </summary>
public class TmdbItemDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; } = -1;

    // TMDb returns either "title" (movie) or "name" (TV show)
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // TMDb returns either "release_date" (movie) or "first_air_date" (TV show)
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Used by camera recognition for ranking and filtering.</summary>
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int> GenreIds { get; set; } = [];

    /// <summary>Movie details only: the film series the movie belongs to.</summary>
    [JsonPropertyName("belongs_to_collection")]
    public TmdbCollectionRefDto? BelongsToCollection { get; set; }


    /// <summary>The title, or the name for a series. <c>Unknown Title</c> when TMDb sent neither, which the daily cleanup treats as an incomplete media row.</summary>
    public string DisplayTitle => Title ?? Name ?? "Unknown Title";
    public string DisplayReleaseDate => ReleaseDate ?? FirstAirDate ?? "Unkown Release Date";
}

/// <summary>
/// The reference to a film series inside a movie's details.
/// </summary>
public class TmdbCollectionRefDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
}

/// <summary>
/// A film series (TMDb "collection") with its movies.
/// </summary>
public class TmdbCollectionDto
{
    [JsonPropertyName("parts")]
    public List<TmdbItemDto> Parts { get; set; } = [];
}

public class TmdbPersonSearchResponseDto
{
    [JsonPropertyName("results")]
    public List<TmdbPersonDto> Results { get; set; } = [];
}

public class TmdbPersonDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }
}

/// <summary>
/// Response of <c>GET person/{id}/combined_credits</c>.
/// </summary>
public class TmdbPersonCreditsDto
{
    [JsonPropertyName("cast")]
    public List<TmdbCreditDto> Cast { get; set; } = [];

    [JsonPropertyName("crew")]
    public List<TmdbCreditDto> Crew { get; set; } = [];
}

/// <summary>
/// A movie or series credit of a person: the title's own fields plus the person's role in it.
/// </summary>
public class TmdbCreditDto : TmdbItemDto
{
    /// <summary><c>movie</c> or <c>tv</c>.</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    /// <summary>Crew only, e.g. <c>Director</c>.</summary>
    [JsonPropertyName("job")]
    public string? Job { get; set; }

    /// <summary>Series only: how many episodes the person was in. A guest appearance is one or two.</summary>
    [JsonPropertyName("episode_count")]
    public int? EpisodeCount { get; set; }
}
