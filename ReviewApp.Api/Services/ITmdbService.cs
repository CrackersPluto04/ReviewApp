using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface ITmdbService
{
    Task<List<TmdbItemDto>> SearchMoviesAsync(string query);
    Task<List<TmdbItemDto>> SearchSeriesAsync(string query);
}
