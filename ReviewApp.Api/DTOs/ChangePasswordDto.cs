using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record ChangePasswordDto
{
    [Required, StringLength(72)]
    public string CurrentPassword { get; init; } = string.Empty;

    // BCrypt only uses the first 72 bytes of a password
    [Required, StringLength(72, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}
