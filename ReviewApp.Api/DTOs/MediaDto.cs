using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// A movie, series or track as the clients see it. It is built from TMDb / Spotify data (or the local cache), and
/// is also the media part of review and collection requests, which is why it carries validation attributes.
/// </summary>
public class MediaDto
{
    /// <summary>
    /// A key that is unique across media types, the external ID with the media type appended (e.g. <c>603Movie</c>),
    /// because TMDb movie and series IDs overlap. Not the database ID and not accepted as an identifier by the API.
    /// </summary>
    [Required]
    public string ID { get; set; } = null!;

    /// <summary>The TMDb or Spotify ID, unique together with <see cref="MediaType"/>.</summary>
    [Required]
    public string ExternalApiID { get; set; } = null!;

    [Required]
    public MediaType MediaType { get; set; }


    [MaxLength(100)]
    public string? Title { get; set; }
    /// <summary>The source's release date text, not normalized (TMDb gives a full date, Spotify may give only a year).</summary>
    public string? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    /// <summary>The artists joined by commas for music. For movies and series it is empty in search results and filled from the title's details.</summary>
    [MaxLength(50)]
    public string? Creator { get; set; }
}
