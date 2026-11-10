using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// The user-written part of a review.
/// </summary>
public class ReviewDto
{
    /// <summary>The score from 1 to 10.</summary>
    [Required]
    [Range(1.0, 10.0, ErrorMessage = "Score must be between 1 and 10.")]
    public decimal Score { get; set; }

    [MaxLength(500)]
    public string? ReviewText { get; set; }
    [MaxLength(250)]
    public string? Pros { get; set; }
    [MaxLength(250)]
    public string? Cons { get; set; }

    /// <summary>Who may see the review. Private unless the author chooses otherwise.</summary>
    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
}
