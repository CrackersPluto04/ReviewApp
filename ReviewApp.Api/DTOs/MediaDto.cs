using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class MediaDto
{
    public string? ExternalApiID { get; set; }

    [Required]
    public MediaType MediaType { get; set; }


    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;
    public string? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    [MaxLength(50)]
    public string? Creator { get; set; }
}
