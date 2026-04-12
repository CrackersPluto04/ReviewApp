using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public class MediaService : IMediaService
{
    private readonly AppDbContext _context;
    public MediaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> AddMediaAsync(MediaDto mediaDto)
    {
        var media = _context.Media.FirstOrDefault(m => m.ExternalApiID == mediaDto.ExternalApiID && m.MediaType == mediaDto.MediaType);
        if (media != null)
        {
            return media.ID;
        }

        Media newMedia;

        switch (mediaDto.MediaType)
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

        newMedia.ExternalApiID = mediaDto.ExternalApiID;
        newMedia.MediaType = mediaDto.MediaType;
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
