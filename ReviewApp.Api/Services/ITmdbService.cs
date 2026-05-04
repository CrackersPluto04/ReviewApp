using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface ITmdbService
{
    Task<TmdbSearchResponseDto> SearchMoviesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDto> DiscoverMoviesAsync(TmdbParams p);
    Task<TmdbSearchResponseDto> DiscoverSeriesAsync(TmdbParams p);
}
