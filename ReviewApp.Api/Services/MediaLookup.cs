using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

/// <summary>
/// The outcome of looking a title up. Unknown and unavailable are kept apart so an outage is never reported to the
/// user as "this title does not exist".
/// </summary>
public enum MediaLookupStatus
{
    Found,
    /// <summary>The source does not know the external ID.</summary>
    Unknown,
    /// <summary>The source failed (outage, timeout); try again later.</summary>
    Unavailable,
    InvalidType
}

/// <summary>
/// The details of a media item straight from its source. Nothing is stored.
/// </summary>
public record MediaLookup(MediaLookupStatus Status, MediaDto? Media = null);

/// <summary>
/// A media row of our database, found or created from its source.
/// </summary>
public record MediaResolution(MediaLookupStatus Status, int MediaID = 0);

/// <summary>
/// Thrown by the services that need a stored media row (reviews, collections) when it cannot be resolved.
/// <c>MediaErrorFilter</c> turns it into 404 <c>media_not_found</c>, 503 <c>source_unavailable</c> or 400.
/// </summary>
public class MediaResolutionException : Exception
{
    public MediaLookupStatus Status { get; }
    public MediaType MediaType { get; }

    public MediaResolutionException(MediaLookupStatus status, MediaType mediaType)
        : base($"Media could not be resolved: {status}.")
    {
        Status = status;
        MediaType = mediaType;
    }
}
