using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _context;

    public MediaController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMedia()
    {
        var mediaList = await _context.Media.Select(m => ToMediaDto(m.MediaType, m)).ToListAsync();
        return Ok(mediaList);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateMedia(MediaDto request)
    {
        Media newMedia;

        switch (request.MediaType)
        {
            case MediaType.Movie:
                newMedia = new Movie();
                break;
            case MediaType.Series:
                newMedia = new Series();
                break;
            case MediaType.Music:
                newMedia = new Music();
                break;
            default:
                return BadRequest(new { error = "Invalid media type." });
        }

        newMedia.ExternalApiID = request.ExternalApiID;
        newMedia.MediaType = request.MediaType;
        newMedia.Title = request.Title;
        // Try parsing the release date, if provided
        if (!string.IsNullOrWhiteSpace(request.ReleaseDate) && DateTime.TryParse(request.ReleaseDate, out var parsedReleaseDate))
            newMedia.ReleaseDate = parsedReleaseDate;
        else
            newMedia.ReleaseDate = null;
        newMedia.PosterUrl = request.PosterUrl;
        newMedia.Overview = request.Overview;
        newMedia.Creator = request.Creator;

        _context.Media.Add(newMedia);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Media added successfully!", id = newMedia.ID });
    }

    [HttpGet("search/movie")]
    public async Task<IActionResult> SearchExternalMovies([FromQuery] string query, [FromServices] ITmdbService tmdbService)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var movies = await tmdbService.SearchMoviesAsync(query);
        var formattedMovies = movies.Select(m => ToMediaDto(MediaType.Movie, m)).ToList();

        return Ok(formattedMovies);
    }

    [HttpGet("search/series")]
    public async Task<IActionResult> SearchExternalSeries([FromQuery] string query, [FromServices] ITmdbService tmdbService)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var series = await tmdbService.SearchSeriesAsync(query);
        var formattedSeries = series.Select(s => ToMediaDto(MediaType.Series, s)).ToList();

        return Ok(formattedSeries);
    }

    [HttpGet("search/music")]
    public async Task<IActionResult> SearchExternalMusic([FromQuery] string query, [FromServices] ISpotifyService spotifyService)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var tracks = await spotifyService.SearchMusicAsync(query);
        var formattedTracks = tracks.Select(t => ToMediaDto(MediaType.Music, spotifyTrack: t)).ToList();

        return Ok(formattedTracks);
    }

    [HttpGet("search/all")]
    public async Task<IActionResult> SearchExternalAll([FromQuery] string query, [FromServices] ITmdbService tmdbService, [FromServices] ISpotifyService spotifyService)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var moviesTask = tmdbService.SearchMoviesAsync(query);
        var seriesTask = tmdbService.SearchSeriesAsync(query);
        var musicsTask = spotifyService.SearchMusicAsync(query);

        await Task.WhenAll(moviesTask, seriesTask, musicsTask);

        var movies = moviesTask.Result.Select(m => ToMediaDto(MediaType.Movie, m));
        var series = seriesTask.Result.Select(s => ToMediaDto(MediaType.Series, s));
        var musics = musicsTask.Result.Select(t => ToMediaDto(MediaType.Music, spotifyTrack: t));

        var combinedResults = movies.Concat(series).Concat(musics).ToList();
        //var randomizedResults = combinedResults.OrderBy(_ => Guid.NewGuid()).ToList();

        return Ok(combinedResults);
    }

    // Helper methods to convert external API results to MediaDto
    private MediaDto ToMediaDto(MediaType type, TmdbItemDto tmdbItem)//SpotifyTrackDto spotifyTrack
    {
        return new MediaDto
        {
            ExternalApiID = tmdbItem.ID.ToString(),
            MediaType = type,
            Title = tmdbItem.DisplayTitle,
            ReleaseDate = tmdbItem.DisplayReleaseDate,
            PosterUrl = string.IsNullOrEmpty(tmdbItem.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{tmdbItem.PosterPath}",
            Overview = tmdbItem.Overview,
            Creator = string.Empty
        };
    }

    private MediaDto ToMediaDto(MediaType type, SpotifyTrackDto spotifyTrack)
    {
        return new MediaDto
        {
            ExternalApiID = spotifyTrack.Id,
            MediaType = type,
            Title = spotifyTrack.Name,
            ReleaseDate = spotifyTrack.Album?.ReleaseDate,
            PosterUrl = spotifyTrack.Album?.Images.FirstOrDefault()?.Url,
            Overview = string.Empty,
            Creator = string.Join(", ", spotifyTrack.Artists?.Select(a => a.Name) ?? [])
        };
    }

    private MediaDto ToMediaDto(MediaType type, Media media)
    {
        return new MediaDto
        {
            ExternalApiID = media.ExternalApiID,
            MediaType = type,
            Title = media.Title,
            ReleaseDate = media.ReleaseDate?.ToString("yyyy-MM-dd"),
            PosterUrl = media.PosterUrl,
            Overview = media.Overview,
            Creator = media.Creator
        };
    }
}
