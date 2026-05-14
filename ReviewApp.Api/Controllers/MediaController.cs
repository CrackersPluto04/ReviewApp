using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
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

    [HttpGet("media/{mediaType}/{externalApiId}")]
    public async Task<IActionResult> GetExternalMediaById([FromRoute] MediaType mediaType, [FromRoute] string externalApiId)
    {
        object? mediaDto = mediaType switch
        {
            MediaType.Movie => await _tmdbService.GetMovieByIdAsync(externalApiId),
            MediaType.Series => await _tmdbService.GetSeriesByIdAsync(externalApiId),
            MediaType.Music => await _spotifyService.GetMusicByIdAsync(externalApiId),
            _ => null
        };

        if (mediaDto == null)
        {
            return BadRequest(new { error = "Invalid media type." });
        }

        MediaDto? formattedMedia = mediaType switch
        {
            MediaType.Movie => _mediaService.ToMediaDto(MediaType.Movie, (TmdbItemDto)mediaDto),
            MediaType.Series => _mediaService.ToMediaDto(MediaType.Series, (TmdbItemDto)mediaDto),
            MediaType.Music => _mediaService.ToMediaDto(MediaType.Music, (SpotifyTrackDto)mediaDto),
            _ => null
        };

        if (formattedMedia == null)
        {
            return BadRequest(new { error = "Invalid media type." });
        }

        return Ok(formattedMedia);
    }

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

        // Formatting media items
        var movies = movieDto.Results.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m));
        var series = seriesDto.Results.Select(s => _mediaService.ToMediaDto(MediaType.Series, s));
        var musics = spotifyDto.Items.Select(t => _mediaService.ToMediaDto(MediaType.Music, t));

        // Combining results and calculating total count
        var combinedResults = movies.Take(5).Concat(series.Take(5)).Concat(musics.Take(5)).ToList();
        var count = combinedResults.Count;

        var response = new PagedResponse<MediaDto>(combinedResults, count, 1, count);
        return Ok(response);
    }

    [HttpGet("discover/movie")]
    public async Task<IActionResult> DiscoverMovies([FromQuery] TmdbParams p)
    {
        var tmdbDto = await _tmdbService.DiscoverMoviesAsync(p);
        var formattedMovies = tmdbDto.Results.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m)).ToList();

        var response = new PagedResponse<MediaDto>(formattedMovies, tmdbDto.TotalCount, p.Page, 20);
        return Ok(response);
    }

    [HttpGet("discover/series")]
    public async Task<IActionResult> DiscoverSeries([FromQuery] TmdbParams p)
    {
        var tmdbDto = await _tmdbService.DiscoverSeriesAsync(p);
        var formattedSeries = tmdbDto.Results.Select(s => _mediaService.ToMediaDto(MediaType.Series, s)).ToList();

        var response = new PagedResponse<MediaDto>(formattedSeries, tmdbDto.TotalCount, p.Page, 20);
        return Ok(response);
    }

    [HttpGet("discover/music")]
    public async Task<IActionResult> DiscoverMusic([FromQuery] SpotifyParams p)
    {
        var spotifyDto = await _spotifyService.DiscoverMusicAsync(p);
        var formattedTracks = spotifyDto.Items.Select(t => _mediaService.ToMediaDto(MediaType.Music, spotifyTrack: t)).ToList();

        var response = new PagedResponse<MediaDto>(formattedTracks, spotifyDto.TotalCount, p.Page, 10);
        return Ok(response);
    }
}