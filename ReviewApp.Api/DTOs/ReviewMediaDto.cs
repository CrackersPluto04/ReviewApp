using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class ReviewMediaDto
{
    [Required]
    public MediaDto MediaDto { get; set; } = null!;
    [Required]
    public ReviewDto ReviewDto { get; set; } = null!;
}
