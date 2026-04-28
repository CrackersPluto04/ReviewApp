using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITmdbService _tmdbService;
    private readonly ISpotifyService _spotifyService;
    private readonly IMediaService _mediaService;

    public MediaController(AppDbContext context, ITmdbService tmdbService, ISpotifyService spotifyService, IMediaService mediaService)
    {
        _context = context;
        _tmdbService = tmdbService;
        _spotifyService = spotifyService;
        _mediaService = mediaService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMedia()
    {
        var mediaList = await _context.Media.Select(m => _mediaService.ToMediaDto(m.MediaType, m)).ToListAsync();
        return Ok(mediaList);
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
}
