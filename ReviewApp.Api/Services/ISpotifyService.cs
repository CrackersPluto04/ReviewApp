using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface ISpotifyService
{
    Task<List<SpotifyTrackDto>> SearchMusicAsync(string query);
}
