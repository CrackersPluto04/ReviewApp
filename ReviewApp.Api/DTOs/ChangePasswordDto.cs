using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record ChangePasswordDto
{
    [Required, StringLength(20)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}
