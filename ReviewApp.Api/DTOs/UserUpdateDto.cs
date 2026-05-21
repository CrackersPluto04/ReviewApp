using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record UserUpdateDto
{
    [MaxLength(150)]
    public string? Bio { get; init; }
    [Url]
    public string? ProfilePictureUrl { get; init; }
}
