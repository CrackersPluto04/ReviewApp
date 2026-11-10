using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Services;

/// <summary>
/// Looks titles up at TMDb / Spotify and caches them as <c>Media</c> rows, keyed by external ID and media type
/// (a unique index). A row is only stored when the source knows the ID, so an unknown ID or an outage never
/// leaves a half-filled row behind. Parallel first requests for the same title are resolved through the unique
/// index: the loser uses the winner's row.
/// </summary>
public class MediaService : IMediaService
{
    /// <summary>The longest external ID accepted. Longer IDs do not exist at TMDb or Spotify, and the column is limited to this.</summary>
    public const int MaxExternalIdLength = 64;

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
        var resolution = await ResolveAsync(type, externalApiId);
        return resolution.Status switch
        {
            MediaLookupStatus.Found => resolution.MediaID,
            MediaLookupStatus.InvalidType => -1,
            _ => throw new MediaResolutionException(resolution.Status, type)
        };
    }

    public async Task<MediaResolution> ResolveAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(type))
            return new MediaResolution(MediaLookupStatus.InvalidType);

        var existing = await FindStoredAsync(type, externalApiId, cancellationToken);
        if (existing != null)
            return new MediaResolution(MediaLookupStatus.Found, existing.Value);

        // Not stored yet: fetch the details from the source, and store nothing if that fails
        var lookup = await LookupAsync(type, externalApiId, cancellationToken);
        if (lookup.Status != MediaLookupStatus.Found)
            return new MediaResolution(lookup.Status);

        Media newMedia = type switch
        {
            MediaType.Movie => new Movie(),
            MediaType.Series => new Series(),
            _ => new Music()
        };
        newMedia.ExternalApiID = externalApiId;
        newMedia.MediaType = type;
        ApplyDetails(newMedia, lookup.Media!);

        _context.Media.Add(newMedia);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new MediaResolution(MediaLookupStatus.Found, newMedia.ID);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A parallel request stored the same media first (unique index on type + external ID): use that row
            _context.Entry(newMedia).State = EntityState.Detached;
            var winner = await FindStoredAsync(type, externalApiId, cancellationToken);
            return winner != null
                ? new MediaResolution(MediaLookupStatus.Found, winner.Value)
                : throw new InvalidOperationException("Media row vanished after a duplicate insert.", ex);
        }
    }

    public async Task<MediaLookup> LookupAsync(MediaType type, string externalApiId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(type))
            return new MediaLookup(MediaLookupStatus.InvalidType);
        if (string.IsNullOrWhiteSpace(externalApiId) || externalApiId.Length > MaxExternalIdLength)
            return new MediaLookup(MediaLookupStatus.Unknown);

        try
        {
            MediaDto? media = type switch
            {
                MediaType.Movie => await _tmdbService.FindMovieByIdAsync(externalApiId, cancellationToken) is { } movie ? ToMediaDto(type, movie) : null,
                MediaType.Series => await _tmdbService.FindSeriesByIdAsync(externalApiId, cancellationToken) is { } series ? ToMediaDto(type, series) : null,
                _ => await _spotifyService.FindTrackByIdAsync(externalApiId, cancellationToken) is { } track ? ToMediaDto(type, track) : null
            };
            return media == null
                ? new MediaLookup(MediaLookupStatus.Unknown)
                : new MediaLookup(MediaLookupStatus.Found, media);
        }
        catch (MediaSourceUnavailableException)
        {
            return new MediaLookup(MediaLookupStatus.Unavailable);
        }
    }

    /// <summary>
    /// Copies the fetched details onto a stored row, for new rows and for the repair of incomplete ones. Text is
    /// cut to the column lengths and an unparseable release date is stored as null.
    /// </summary>
    /// <param name="target">The stored row to fill.</param>
    /// <param name="details">The details fetched from the source.</param>
    public static void ApplyDetails(Media target, MediaDto details)
    {
        target.Title = Truncate(details.Title, 100);
        target.ReleaseDate = !string.IsNullOrWhiteSpace(details.ReleaseDate) && DateTime.TryParse(details.ReleaseDate, out var parsedReleaseDate)
            ? parsedReleaseDate
            : null;
        target.PosterUrl = details.PosterUrl;
        target.Overview = details.Overview;
        target.Creator = Truncate(details.Creator, 50);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value == null || value.Length <= maxLength ? value : value[..maxLength];

    private async Task<int?> FindStoredAsync(MediaType type, string externalApiId, CancellationToken cancellationToken) =>
        await _context.Media
            .Where(m => m.ExternalApiID == externalApiId && m.MediaType == type)
            .Select(m => (int?)m.ID)
            .FirstOrDefaultAsync(cancellationToken);

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
