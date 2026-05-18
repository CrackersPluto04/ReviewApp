using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public class MediaService : IMediaService
{
    private readonly AppDbContext _context;
    private readonly ITmdbService _tmdbService;
    private readonly ISpotifyService _spotifyService;

    public MediaService(AppDbContext context, ITmdbService tmdbService, ISpotifyService spotifyService)
    {
        _context = context;
        _tmdbService = tmdbService;
        _spotifyService = spotifyService;
    }

    public async Task<int> GetOrCreateMediaAsync(MediaType type, string externalApiId)
    {
        // First, check if the media already exists in the database
        var media = _context.Media.FirstOrDefault(m => m.ExternalApiID == externalApiId && m.MediaType == type);
        if (media != null)
            return media.ID;

        // If not, fetch details from the appropriate external API and create a new media entry
        MediaDto? mediaDto = type switch
        {
            MediaType.Movie => this.ToMediaDto(type, await _tmdbService.GetMovieByIdAsync(externalApiId)),
            MediaType.Series => this.ToMediaDto(type, await _tmdbService.GetSeriesByIdAsync(externalApiId)),
            MediaType.Music => this.ToMediaDto(type, await _spotifyService.GetMusicByIdAsync(externalApiId)),
            _ => null
        };
        if (mediaDto == null)
            return -1; // Invalid media type or failed to fetch media details

        // Create a new media entry based on the fetched details and add to database
        Media newMedia;

        switch (type)
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
                return -1; // Invalid media type
        }

        newMedia.ExternalApiID = externalApiId;
        newMedia.MediaType = type;
        newMedia.Title = mediaDto.Title;
        // Try parsing the release date, if provided
        if (!string.IsNullOrWhiteSpace(mediaDto.ReleaseDate) && DateTime.TryParse(mediaDto.ReleaseDate, out var parsedReleaseDate))
            newMedia.ReleaseDate = parsedReleaseDate;
        else
            newMedia.ReleaseDate = null;
        newMedia.PosterUrl = mediaDto.PosterUrl;
        newMedia.Overview = mediaDto.Overview;
        newMedia.Creator = mediaDto.Creator;

        _context.Media.Add(newMedia);
        await _context.SaveChangesAsync();

        return newMedia.ID;
    }

    public MediaDto ToMediaDto(MediaType type, TmdbItemDto tmdbItem)
    {
        return new MediaDto
        {
            ID = tmdbItem.ID.ToString() + type.ToString(),
            ExternalApiID = tmdbItem.ID.ToString(),
            MediaType = type,
            Title = tmdbItem.DisplayTitle,
            ReleaseDate = tmdbItem.DisplayReleaseDate,
            PosterUrl = string.IsNullOrEmpty(tmdbItem.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{tmdbItem.PosterPath}",
            Overview = tmdbItem.Overview,
            Creator = string.Empty
        };
    }

    public MediaDto ToMediaDto(MediaType type, SpotifyTrackDto spotifyTrack)
    {
        return new MediaDto
        {
            ID = spotifyTrack.Id + type.ToString(),
            ExternalApiID = spotifyTrack.Id,
            MediaType = type,
            Title = spotifyTrack.Name,
            ReleaseDate = spotifyTrack.Album?.ReleaseDate,
            PosterUrl = spotifyTrack.Album?.Images.FirstOrDefault()?.Url,
            Overview = string.Empty,
            Creator = string.Join(", ", spotifyTrack.Artists?.Select(a => a.Name) ?? [])
        };
    }

    public MediaDto ToMediaDto(MediaType type, Media media)
    {
        return new MediaDto
        {
            ID = media.ExternalApiID + type.ToString(),
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
