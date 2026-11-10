using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Request to change the password. The length limits are the same as at registration
/// (see <see cref="UserRegisterDto.Password"/>).
/// </summary>
public record ChangePasswordDto
{
    [Required, StringLength(20)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}
