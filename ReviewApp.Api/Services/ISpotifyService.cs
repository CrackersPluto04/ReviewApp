using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface ISpotifyService
{
    Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1);
    Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p);
}
