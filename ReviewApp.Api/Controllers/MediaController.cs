using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Data;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Models;
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
        var mediaList = await _context.Media.ToListAsync();
        return Ok(mediaList);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateMedia(MediaCreateDto request)
    {
        Media newMedia;

        switch (request.MediaType)
        {
            case Enums.MediaType.Movie:
                newMedia = new Movie();
                break;
            case Enums.MediaType.Series:
                newMedia = new Series();
                break;
            case Enums.MediaType.Music:
                newMedia = new Music();
                break;
            default:
                return BadRequest(new { error = "Invalid media type." });
        }

        newMedia.Title = request.Title;
        newMedia.ReleaseDate = request.ReleaseDate;
        newMedia.PosterUrl = request.PosterUrl;
        newMedia.Overview = request.Overview;
        newMedia.Creator = request.Creator;
        newMedia.MediaType = request.MediaType;
        newMedia.ExternalApiID = request.ExternalApiID;

        _context.Media.Add(newMedia);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Media added successfully!", id = newMedia.ID });
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchExternalMovies([FromQuery] string query, [FromServices] ITmdbService tmdbService)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query parameter is required." });
        }

        var results = await tmdbService.SearchMoviesAsync(query);
        var formattedResults = results.Select(m => new
        {
            ExternalApiID = m.ID.ToString(),
            MediaType = Enums.MediaType.Movie,
            Title = m.Title,
            ReleaseDate = m.ReleaseDate,
            PosterUrl = string.IsNullOrEmpty(m.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{m.PosterPath}",
            Overview = m.Overview
        });

        return Ok(formattedResults);
    }
}
