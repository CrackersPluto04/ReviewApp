using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public enum MediaLookupStatus
{
    Found,
    // The source doesn't know the external ID
    Unknown,
    // The source failed (outage, timeout): try again later
    Unavailable,
    InvalidType
}

// The details of a media item straight from its source (nothing stored)
public record MediaLookup(MediaLookupStatus Status, MediaDto? Media = null);

// A media row of our database, found or created from its source
public record MediaResolution(MediaLookupStatus Status, int MediaID = 0);

// Thrown by the services that need a stored media row (reviews, collections) when it can't be resolved.
// MediaErrorFilter turns it into 404 media_not_found / 503 source_unavailable / 400.
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
