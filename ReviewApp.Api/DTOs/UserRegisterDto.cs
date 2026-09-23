using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public class UserRegisterDto
{
    [Required, StringLength(20, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    // BCrypt only uses the first 72 bytes of a password
    [Required, StringLength(72, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
