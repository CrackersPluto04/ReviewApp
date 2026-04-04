using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class ReviewCreateDto
{
    [Required]
    public int MediaID { get; set; }

    [Required]
    [Range(1.0, 10.0, ErrorMessage = "Score must be between 1 and 10.")]
    public decimal Score { get; set; }

    [MaxLength(250)]
    public string? ReviewText { get; set; }
    [MaxLength(500)]
    public string? Pros { get; set; }
    [MaxLength(500)]
    public string? Cons { get; set; }

    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
}
