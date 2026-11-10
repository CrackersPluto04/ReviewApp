using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A movie, series or track, cached from TMDb / Spotify the first time anyone uses it. Mapped as table-per-hierarchy
/// with <see cref="MediaType"/> as the discriminator. A row is only stored when the source knows the ID.
/// </summary>
public abstract class Media
{
    public int ID { get; set; }

    /// <summary>
    /// The TMDb or Spotify ID. Limited so it can be part of the unique (<c>MediaType</c>, <c>ExternalApiID</c>)
    /// index; the real IDs are far shorter.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ExternalApiID { get; set; } = null!;

    [Required]
    public MediaType MediaType { get; set; }

    /// <summary>Null or the placeholder <c>Unknown Title</c> marks a row left incomplete by an old failed lookup, which the daily cleanup repairs.</summary>
    [MaxLength(100)]
    public string? Title { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    /// <summary>The artists for music, the creator or director for movies and series.</summary>
    [MaxLength(50)]
    public string? Creator { get; set; }
}

/// <summary>A movie (TMDb).</summary>
public class Movie : Media { }

/// <summary>A TV series (TMDb).</summary>
public class Series : Media { }

/// <summary>A track (Spotify).</summary>
public class Music : Media { }
