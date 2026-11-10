using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Request body for creating or editing a review: the title it is about and the review itself. The review is
/// identified by the user and the title, so a create and an edit have the same shape.
/// </summary>
public class ReviewMediaDto
{
    [Required]
    public MediaDto MediaDto { get; set; } = null!;
    [Required]
    public ReviewDto ReviewDto { get; set; } = null!;
}
