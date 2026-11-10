using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Access to TMDb for movies, series, film series and people. The first group of methods serves search and
/// browsing, the strict lookups serve storing media, and the last group serves camera recognition.
/// </summary>
public interface ITmdbService
{
    /// <summary>Searches movies by title. Pages hold 20 results.</summary>
    /// <param name="query">The title to search for.</param>
    /// <param name="page">1-based page number.</param>
    Task<TmdbSearchResponseDto> SearchMoviesAsync(string query, int page = 1);

    /// <summary>Searches series by title. Pages hold 20 results.</summary>
    /// <param name="query">The title to search for.</param>
    /// <param name="page">1-based page number.</param>
    Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1);

    /// <summary>Browses movies by filters instead of a text query.</summary>
    Task<TmdbSearchResponseDto> DiscoverMoviesAsync(TmdbParams p);

    /// <summary>Browses series by filters instead of a text query.</summary>
    Task<TmdbSearchResponseDto> DiscoverSeriesAsync(TmdbParams p);

    /// <summary>Fetches one movie's details by ID for display.</summary>
    Task<TmdbItemDto> GetMovieByIdAsync(string id);

    /// <summary>Fetches one series' details by ID for display.</summary>
    Task<TmdbItemDto> GetSeriesByIdAsync(string id);

    /// <summary>
    /// Strict lookup by ID, used before storing a media row, so an unknown ID is never stored.
    /// </summary>
    /// <returns>The movie, or null when TMDb does not know the ID.</returns>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed (outage, timeout), which is kept apart from "unknown".</exception>
    Task<TmdbItemDto?> FindMovieByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Strict lookup of a series by ID; see <see cref="FindMovieByIdAsync"/>.</summary>
    Task<TmdbItemDto?> FindSeriesByIdAsync(string id, CancellationToken cancellationToken);

    // Lookups of camera recognition. Unlike the methods above, these throw MediaSourceUnavailableException
    // when TMDb fails, so an outage is never mistaken for "not found". Adult titles are excluded.

    /// <summary>Camera recognition: the first page of the movie title search.</summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<List<TmdbItemDto>> FindMoviesAsync(string title, CancellationToken cancellationToken);

    /// <summary>Camera recognition: the first page of the series title search.</summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<List<TmdbItemDto>> FindSeriesAsync(string title, CancellationToken cancellationToken);

    /// <summary>Camera recognition: the ID of the collection (film series) a movie belongs to.</summary>
    /// <returns>The collection ID, or null if the movie belongs to none.</returns>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<int?> GetMovieCollectionIdAsync(int movieId, CancellationToken cancellationToken);

    /// <summary>Camera recognition: the movies of a collection.</summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<List<TmdbItemDto>> GetCollectionPartsAsync(int collectionId, CancellationToken cancellationToken);

    /// <summary>Camera recognition: searches people by name.</summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<List<TmdbPersonDto>> FindPeopleAsync(string name, CancellationToken cancellationToken);

    /// <summary>Camera recognition: the movies and series of a person, as cast (actors) and crew (directors and others).</summary>
    /// <exception cref="Recognition.MediaSourceUnavailableException">TMDb failed.</exception>
    Task<TmdbPersonCreditsDto> GetPersonCreditsAsync(int personId, CancellationToken cancellationToken);
}
