using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

public interface IMediaService
{
    // The ID of the stored media row, created from its source if needed. -1 for an invalid type;
    // throws MediaResolutionException when the source doesn't know the ID or is unavailable (nothing is stored then)
    Task<int> GetOrCreateMediaAsync(MediaType type, string externalApiId);
    // Same, without exceptions: the status says why there is no row
    Task<MediaResolution> ResolveAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default);
    // The details straight from the source, nothing stored
    Task<MediaLookup> LookupAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default);
    MediaDto ToMediaDto(MediaType type, TmdbItemDto tmdbItem);
    MediaDto ToMediaDto(MediaType type, SpotifyTrackDto spotifyTrack);
    MediaDto ToMediaDto(MediaType type, Media media);
}
