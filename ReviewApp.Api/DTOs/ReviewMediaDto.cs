using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class ReviewMediaDto
{
    [Required]
    public MediaDto Media { get; set; } = null!;
    [Required]
    public ReviewDto Review { get; set; } = null!;
}
