using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

public class SpotifyTokenDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
}

public class SpotifySearchResponseDto
{
    [JsonPropertyName("tracks")]
    public SpotifyTracksDto? Tracks { get; set; }
}

public class SpotifyTracksDto
{
    [JsonPropertyName("items")]
    public List<SpotifyTrackDto> Items { get; set; } = new();
}

public class SpotifyTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public SpotifyAlbumDto? Album { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtistDto> Artists { get; set; } = new();
}

public class SpotifyAlbumDto
{
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto> Images { get; set; } = new();
}

public class SpotifyArtistDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SpotifyImageDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
