using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Search, discovery and lookup of movies, series and music through TMDb and Spotify. Anonymous, but every call can
/// reach a third-party API, so the whole controller sits under the <c>external-api</c> rate limit that protects
/// their quotas.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting("external-api")]
public class MediaController : ControllerBase
{
    private readonly ITmdbService _tmdbService;
    private readonly ISpotifyService _spotifyService;
    private readonly IMediaService _mediaService;

    public MediaController(ITmdbService tmdbService, ISpotifyService spotifyService, IMediaService mediaService)
    {
        _tmdbService = tmdbService;
        _spotifyService = spotifyService;
        _mediaService = mediaService;
    }

    /// <summary>
    /// Looks up one title by its external ID, from the local cache or else from its source.
    /// </summary>
    /// <param name="mediaType">Movie, series or music; decides the source.</param>
    /// <param name="externalApiId">The TMDb or Spotify ID.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects, which also abandons the source call.</param>
    /// <returns>The title.</returns>
    /// <response code="200">The title.</response>
    /// <response code="404">The source does not know this ID (<c>media_not_found</c>).</response>
    /// <response code="503">The source could not be reached (<c>source_unavailable</c>). Kept apart from 404 so clients don't treat an outage as "does not exist".</response>
    [HttpGet("media/{mediaType}/{externalApiId}")]
    public async Task<IActionResult> GetExternalMediaById([FromRoute] MediaType mediaType, [FromRoute] string externalApiId, CancellationToken cancellationToken)
    {
        var lookup = await _mediaService.LookupAsync(mediaType, externalApiId, cancellationToken);
        return lookup.Status switch
        {
            MediaLookupStatus.Found => Ok(lookup.Media),
            MediaLookupStatus.Unknown => NotFound(new { error = MediaErrorFilter.NotFoundMessage, code = MediaErrorFilter.NotFoundCode }),
            MediaLookupStatus.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = MediaErrorFilter.UnavailableMessage, code = MediaErrorFilter.UnavailableCode }),
            _ => BadRequest(new { error = "Invalid media type." })
        };
    }

    /// <summary>
    /// Searches TMDb for movies by title.
    /// </summary>
    /// <param name="query">The search text; must not be blank.</param>
    /// <param name="page">1-based page number.</param>
    /// <returns>A page of 20 movies with the total result count.</returns>
    /// <response code="200">The page of results.</response>
    /// <response code="400">The query is blank.</response>
    [HttpGet("search/movie")]
    public async Task<IActionResult> SearchExternalMovies([FromQuery] string query, [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var tmdbDto = await _tmdbService.SearchMoviesAsync(query, page);
        var formattedMovies = tmdbDto.Results.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m)).ToList();

        var response = new PagedResponse<MediaDto>(formattedMovies, tmdbDto.TotalCount, page, 20);
        return Ok(response);
    }

    /// <summary>
    /// Searches TMDb for series by title.
    /// </summary>
    /// <param name="query">The search text; must not be blank.</param>
    /// <param name="page">1-based page number.</param>
    /// <returns>A page of 20 series with the total result count.</returns>
    /// <response code="200">The page of results.</response>
    /// <response code="400">The query is blank.</response>
    [HttpGet("search/series")]
    public async Task<IActionResult> SearchExternalSeries([FromQuery] string query, [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var tmdbDto = await _tmdbService.SearchSeriesAsync(query, page);
        var formattedSeries = tmdbDto.Results.Select(s => _mediaService.ToMediaDto(MediaType.Series, s)).ToList();

        var response = new PagedResponse<MediaDto>(formattedSeries, tmdbDto.TotalCount, page, 20);
        return Ok(response);
    }

    /// <summary>
    /// Searches Spotify for tracks.
    /// </summary>
    /// <param name="query">The search text; must not be blank.</param>
    /// <param name="page">1-based page number.</param>
    /// <returns>A page of up to 10 tracks, Spotify's page size here, with the total result count.</returns>
    /// <response code="200">The page of results.</response>
    /// <response code="400">The query is blank.</response>
    [HttpGet("search/music")]
    public async Task<IActionResult> SearchExternalMusic([FromQuery] string query, [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var spotifyDto = await _spotifyService.SearchMusicAsync(query, page);
        var formattedTracks = spotifyDto.Items.Select(t => _mediaService.ToMediaDto(MediaType.Music, spotifyTrack: t)).ToList();

        var response = new PagedResponse<MediaDto>(formattedTracks, spotifyDto.TotalCount, page, 20);
        return Ok(response);
    }

    /// <summary>
    /// Searches movies, series and music at once for the quick-search box. The three sources are queried in
    /// parallel and the first five hits of each are combined, so no kind of media crowds out the others.
    /// </summary>
    /// <param name="query">The search text; must not be blank.</param>
    /// <returns>Up to 15 titles, movies first, then series, then music. A single page: there is no paging.</returns>
    /// <response code="200">The combined results.</response>
    /// <response code="400">The query is blank.</response>
    [HttpGet("search/all")]
    public async Task<IActionResult> SearchExternalAll([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var movieTask = _tmdbService.SearchMoviesAsync(query);
        var seriesTask = _tmdbService.SearchSeriesAsync(query);
        var spotifyTask = _spotifyService.SearchMusicAsync(query);

        await Task.WhenAll(movieTask, seriesTask, spotifyTask);

        var movieDto = await movieTask;
        var seriesDto = await seriesTask;
        var spotifyDto = await spotifyTask;

        var movies = movieDto.Results.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m));
        var series = seriesDto.Results.Select(s => _mediaService.ToMediaDto(MediaType.Series, s));
        var musics = spotifyDto.Items.Select(t => _mediaService.ToMediaDto(MediaType.Music, t));

        var combinedResults = movies.Take(5).Concat(series.Take(5)).Concat(musics.Take(5)).ToList();
        var count = combinedResults.Count;

        var response = new PagedResponse<MediaDto>(combinedResults, count, 1, count);
        return Ok(response);
    }

    /// <summary>
    /// Browses TMDb movies by filters (genre, year, sort order and so on) instead of a text query.
    /// </summary>
    /// <param name="p">The filter and paging parameters, bound from the query string.</param>
    /// <returns>A page of 20 movies with the total result count.</returns>
    /// <response code="200">The page of results.</response>
    [HttpGet("discover/movie")]
    public async Task<IActionResult> DiscoverMovies([FromQuery] TmdbParams p)
    {
        var tmdbDto = await _tmdbService.DiscoverMoviesAsync(p);
        var formattedMovies = tmdbDto.Results.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m)).ToList();

        var response = new PagedResponse<MediaDto>(formattedMovies, tmdbDto.TotalCount, p.Page, 20);
        return Ok(response);
    }

    /// <summary>
    /// Browses TMDb series by filters instead of a text query.
    /// </summary>
    /// <param name="p">The filter and paging parameters, bound from the query string.</param>
    /// <returns>A page of 20 series with the total result count.</returns>
    /// <response code="200">The page of results.</response>
    [HttpGet("discover/series")]
    public async Task<IActionResult> DiscoverSeries([FromQuery] TmdbParams p)
    {
        var tmdbDto = await _tmdbService.DiscoverSeriesAsync(p);
        var formattedSeries = tmdbDto.Results.Select(s => _mediaService.ToMediaDto(MediaType.Series, s)).ToList();

        var response = new PagedResponse<MediaDto>(formattedSeries, tmdbDto.TotalCount, p.Page, 20);
        return Ok(response);
    }

    /// <summary>
    /// Browses Spotify tracks by filters instead of a text query.
    /// </summary>
    /// <param name="p">The filter and paging parameters, bound from the query string.</param>
    /// <returns>A page of 10 tracks with the total result count. Smaller pages than the TMDb endpoints.</returns>
    /// <response code="200">The page of results.</response>
    [HttpGet("discover/music")]
    public async Task<IActionResult> DiscoverMusic([FromQuery] SpotifyParams p)
    {
        var spotifyDto = await _spotifyService.DiscoverMusicAsync(p);
        var formattedTracks = spotifyDto.Items.Select(t => _mediaService.ToMediaDto(MediaType.Music, spotifyTrack: t)).ToList();

        var response = new PagedResponse<MediaDto>(formattedTracks, spotifyDto.TotalCount, p.Page, 10);
        return Ok(response);
    }
}