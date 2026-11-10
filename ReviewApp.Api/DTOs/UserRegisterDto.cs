using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Registration request. The username is also checked against <c>UsernameRules</c> (allowed characters and reserved
/// words), because usernames are part of profile URLs.
/// </summary>
public class UserRegisterDto
{
    [Required, StringLength(20, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 8 to 20 characters. The cap keeps the password under BCrypt's 72-byte limit even with multi-byte characters
    /// (each UTF-16 unit is at most 3 UTF-8 bytes), so no separate byte check is needed. Do not raise it above 24
    /// without adding one.
    /// </summary>
    [Required, StringLength(20, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
