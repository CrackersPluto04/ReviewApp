using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

public class TmdbSearchResponseDto
{
    [JsonPropertyName("results")]
    public List<TmdbItemDto> Results { get; set; } = new();
}

public class TmdbItemDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; }

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


    public string DisplayTitle => Title ?? Name ?? "Unknown Title";
    public string DisplayReleaseDate => ReleaseDate ?? FirstAirDate ?? "Unkown Release Date";
}
