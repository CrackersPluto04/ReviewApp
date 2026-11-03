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

    /* Lookups of camera recognition. Unlike the methods above, these throw MediaSourceUnavailableException
       when TMDb fails, so an outage is never mistaken for "not found". Adult titles are excluded. */

    // First page of the title search
    Task<List<TmdbItemDto>> FindMoviesAsync(string title, CancellationToken cancellationToken);
    Task<List<TmdbItemDto>> FindSeriesAsync(string title, CancellationToken cancellationToken);
    // The collection (film series) a movie belongs to, if any
    Task<int?> GetMovieCollectionIdAsync(int movieId, CancellationToken cancellationToken);
    Task<List<TmdbItemDto>> GetCollectionPartsAsync(int collectionId, CancellationToken cancellationToken);
    Task<List<TmdbPersonDto>> FindPeopleAsync(string name, CancellationToken cancellationToken);
    // Movies and series of a person, as cast (actors) and crew (directors etc.)
    Task<TmdbPersonCreditsDto> GetPersonCreditsAsync(int personId, CancellationToken cancellationToken);
}
