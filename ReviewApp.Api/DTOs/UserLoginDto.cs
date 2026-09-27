using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class UserLoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    // BCrypt only uses the first 72 bytes of a password
    [Required, StringLength(72)]
    public string Password { get; set; } = string.Empty;
}
