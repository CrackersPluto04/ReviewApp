using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

public class TmdbSearchResponseDto
{
    [JsonPropertyName("results")]
    public List<TmdbMovieDto> Results { get; set; } = new();
}

public class TmdbMovieDto
{
    [JsonPropertyName("id")]
    public int ID { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
}
