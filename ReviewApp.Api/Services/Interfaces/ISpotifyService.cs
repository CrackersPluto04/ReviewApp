using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Access to Spotify's track catalogue.
/// </summary>
public interface ISpotifyService
{
    /// <summary>Searches tracks by text. Pages hold 10 tracks.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="page">1-based page number.</param>
    Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1);

    /// <summary>Browses tracks by genre and year. Spotify has no discover call, so this is a filtered search.</summary>
    Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p);

    /// <summary>Fetches one track by ID for display.</summary>
    Task<SpotifyTrackDto> GetMusicByIdAsync(string id);

    /// <summary>
    /// Strict lookup by ID, used before storing a media row, so an unknown ID is never stored.
    /// </summary>
    /// <returns>The track, or null when Spotify does not know the ID.</returns>
    /// <exception cref="Recognition.MediaSourceUnavailableException">Spotify failed (outage, timeout), which is kept apart from "unknown".</exception>
    Task<SpotifyTrackDto?> FindTrackByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Camera recognition: finds tracks matching a track or album title, optionally by an artist.
    /// </summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">Spotify failed, so an outage is never mistaken for "not found".</exception>
    Task<List<SpotifyTrackDto>> FindTracksAsync(string title, string? artist, CancellationToken cancellationToken);
}
