using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public abstract class Media
{
    public int ID { get; set; }
    public string? ExternalApiID { get; set; }

    [Required]
    public MediaType MediaType { get; set; }

    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    [MaxLength(50)]
    public string? Creator { get; set; }
}

public class Movie : Media { }

public class Series : Media { }

public class Music : Media { }
