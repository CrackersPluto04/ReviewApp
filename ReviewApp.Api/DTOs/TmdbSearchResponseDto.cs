using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

public class TmdbSearchResponseDto
{
    [JsonPropertyName("results")]
    public List<TmdbItemDto> Results { get; set; } = [];

    [JsonPropertyName("total_results")]
    public int TotalCount { get; set; } = 0;
}

public class TmdbItemDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; } = -1;

    // TMDb can return either "title" or "name"
    // depending on the type of media (movie vs TV show)
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // TMDb can return either "release_date" or "first_air_date"
    // depending on the type of media (movie vs TV show)
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    // Used by camera recognition for ranking and filtering
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int> GenreIds { get; set; } = [];

    // Movie details only: the film series the movie belongs to
    [JsonPropertyName("belongs_to_collection")]
    public TmdbCollectionRefDto? BelongsToCollection { get; set; }


    public string DisplayTitle => Title ?? Name ?? "Unknown Title";
    public string DisplayReleaseDate => ReleaseDate ?? FirstAirDate ?? "Unkown Release Date";
}

public class TmdbCollectionRefDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
}

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

// GET person/{id}/combined_credits
public class TmdbPersonCreditsDto
{
    [JsonPropertyName("cast")]
    public List<TmdbCreditDto> Cast { get; set; } = [];

    [JsonPropertyName("crew")]
    public List<TmdbCreditDto> Crew { get; set; } = [];
}

public class TmdbCreditDto : TmdbItemDto
{
    // "movie" or "tv"
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    // Crew only, e.g. "Director"
    [JsonPropertyName("job")]
    public string? Job { get; set; }

    // Series only: how many episodes the person was in (a guest appearance is one or two)
    [JsonPropertyName("episode_count")]
    public int? EpisodeCount { get; set; }
}
