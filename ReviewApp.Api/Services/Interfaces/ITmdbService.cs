using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface ITmdbService
{
    Task<TmdbSearchResponseDto> SearchMoviesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDto> DiscoverMoviesAsync(TmdbParams p);
    Task<TmdbSearchResponseDto> DiscoverSeriesAsync(TmdbParams p);
    Task<TmdbItemDto> GetMovieByIdAsync(string id);
    Task<TmdbItemDto> GetSeriesByIdAsync(string id);
}
