using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface ISpotifyService
{
    Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1);
    Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p);
    Task<SpotifyTrackDto> GetMusicByIdAsync(string id);

    // Strict lookup by ID, used before storing a media row: null = Spotify doesn't know the ID,
    // MediaSourceUnavailableException = Spotify failed (outage, timeout)
    Task<SpotifyTrackDto?> FindTrackByIdAsync(string id, CancellationToken cancellationToken);

    // Camera recognition: tracks matching a track or album title, optionally by an artist. Throws
    // MediaSourceUnavailableException when Spotify fails, so an outage is never mistaken for "not found".
    Task<List<SpotifyTrackDto>> FindTracksAsync(string title, string? artist, CancellationToken cancellationToken);
}
