using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface ISpotifyService
{
    Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1);
    Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p);
    Task<SpotifyTrackDto> GetMusicByIdAsync(string id);

    // Camera recognition: tracks matching a track or album title, optionally by an artist. Throws
    // MediaSourceUnavailableException when Spotify fails, so an outage is never mistaken for "not found".
    Task<List<SpotifyTrackDto>> FindTracksAsync(string title, string? artist, CancellationToken cancellationToken);
}
