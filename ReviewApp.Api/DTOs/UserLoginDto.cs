using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Login request. Credentials travel as plain JSON over HTTPS by design.
/// </summary>
public class UserLoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Capped at 20 characters like every password (see <see cref="UserRegisterDto.Password"/>).</summary>
    [Required, StringLength(20)]
    public string Password { get; set; } = string.Empty;
}
