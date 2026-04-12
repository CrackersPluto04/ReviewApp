using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public interface IMediaService
{
    Task<int> AddMediaAsync(MediaDto mediaDto);
    MediaDto ToMediaDto(MediaType type, TmdbItemDto tmdbItem);
    MediaDto ToMediaDto(MediaType type, SpotifyTrackDto spotifyTrack);
    MediaDto ToMediaDto(MediaType type, Media media);
}
