using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
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
    public async Task<IActionResult> SearchExternalMovies([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var movies = await _tmdbService.SearchMoviesAsync(query);
        var formattedMovies = movies.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m)).ToList();

        return Ok(formattedMovies);
    }

    [HttpGet("search/series")]
    public async Task<IActionResult> SearchExternalSeries([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var series = await _tmdbService.SearchSeriesAsync(query);
        var formattedSeries = series.Select(s => _mediaService.ToMediaDto(MediaType.Series, s)).ToList();

        return Ok(formattedSeries);
    }

    [HttpGet("search/music")]
    public async Task<IActionResult> SearchExternalMusic([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var tracks = await _spotifyService.SearchMusicAsync(query);
        var formattedTracks = tracks.Select(t => _mediaService.ToMediaDto(MediaType.Music, spotifyTrack: t)).ToList();

        return Ok(formattedTracks);
    }

    [HttpGet("search/all")]
    public async Task<IActionResult> SearchExternalAll([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var moviesTask = _tmdbService.SearchMoviesAsync(query);
        var seriesTask = _tmdbService.SearchSeriesAsync(query);
        var musicsTask = _spotifyService.SearchMusicAsync(query);

        await Task.WhenAll(moviesTask, seriesTask, musicsTask);

        var movies = moviesTask.Result.Select(m => _mediaService.ToMediaDto(MediaType.Movie, m));
        var series = seriesTask.Result.Select(s => _mediaService.ToMediaDto(MediaType.Series, s));
        var musics = musicsTask.Result.Select(t => _mediaService.ToMediaDto(MediaType.Music, spotifyTrack: t));

        var combinedResults = movies.Concat(series).Concat(musics).ToList();
        //var randomizedResults = combinedResults.OrderBy(_ => Guid.NewGuid()).ToList();

        return Ok(combinedResults);
    }
}
