using System.Text.Json.Serialization;

namespace ReviewApp.Api.DTOs;

// Mirrors of the parts of Spotify's Web API JSON that the app reads. Used only to deserialize Spotify's
// responses and never returned to clients; property names are mapped to Spotify's snake_case.

/// <summary>
/// Spotify's client-credentials token response.
/// </summary>
public class SpotifyTokenDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Lifetime of the token in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Spotify's track search response.
/// </summary>
public class SpotifySearchResponseDto
{
    [JsonPropertyName("tracks")]
    public SpotifyTracksDto Tracks { get; set; } = new SpotifyTracksDto();
}

public class SpotifyTracksDto
{
    /// <summary>Total matches across all pages.</summary>
    [JsonPropertyName("total")]
    public int TotalCount { get; set; } = 0;

    [JsonPropertyName("items")]
    public List<SpotifyTrackDto> Items { get; set; } = [];
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
    public List<SpotifyArtistDto> Artists { get; set; } = [];

    /// <summary>0 to 100. Missing for some apps, in which case it reads as 0.</summary>
    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }
}

public class SpotifyAlbumDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto> Images { get; set; } = [];
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
