using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface ITmdbService
{
    Task<List<TmdbMovieDto>> SearchMoviesAsync(string query);
}
