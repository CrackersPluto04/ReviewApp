using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record UserUpdateDto
{
    [StringLength(20, MinimumLength = 3)]
    public string? Username { get; init; }
    [MaxLength(150)]
    public string? Bio { get; init; }
    [Url]
    public string? ProfilePictureUrl { get; init; }
}
