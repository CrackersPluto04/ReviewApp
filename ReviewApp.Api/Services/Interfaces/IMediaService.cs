using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Stores and looks up media. A row is only stored when the source (TMDb / Spotify) knows the ID, so reviews and
/// collections never point at media that does not exist.
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Gets the stored media row's ID, creating the row from its source if needed.
    /// </summary>
    /// <param name="type">Movie, series or music.</param>
    /// <param name="externalApiId">The TMDb or Spotify ID.</param>
    /// <returns>The stored media ID, or -1 for an invalid type.</returns>
    /// <exception cref="MediaResolutionException">The source does not know the ID or is unavailable; nothing is stored then.</exception>
    Task<int> GetOrCreateMediaAsync(MediaType type, string externalApiId);

    /// <summary>Same as <see cref="GetOrCreateMediaAsync"/> without exceptions: the status says why there is no row.</summary>
    Task<MediaResolution> ResolveAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default);

    /// <summary>Fetches the details straight from the source or the local cache. Nothing is stored.</summary>
    Task<MediaLookup> LookupAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default);

    /// <summary>Converts a TMDb item to the client shape.</summary>
    MediaDto ToMediaDto(MediaType type, TmdbItemDto tmdbItem);

    /// <summary>Converts a Spotify track to the client shape.</summary>
    MediaDto ToMediaDto(MediaType type, SpotifyTrackDto spotifyTrack);

    /// <summary>Converts a stored media row to the client shape.</summary>
    MediaDto ToMediaDto(MediaType type, Media media);
}
