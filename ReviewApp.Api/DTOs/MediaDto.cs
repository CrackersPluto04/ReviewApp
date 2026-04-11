using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class MediaDto
{
    [Required]
    public string ExternalApiID { get; set; } = null!;

    [Required]
    public MediaType MediaType { get; set; }


    [MaxLength(100)]
    public string? Title { get; set; }
    public string? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    [MaxLength(50)]
    public string? Creator { get; set; }
}
