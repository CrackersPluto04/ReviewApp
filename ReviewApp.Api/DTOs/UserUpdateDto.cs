using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Partial update of the caller's profile (PATCH): only the fields that are sent change.
/// </summary>
public record UserUpdateDto
{
    /// <summary>Also checked against <c>UsernameRules</c> (allowed characters, reserved words), because usernames are part of URLs.</summary>
    [StringLength(20, MinimumLength = 3)]
    public string? Username { get; init; }
    [MaxLength(150)]
    public string? Bio { get; init; }
    [Url]
    public string? ProfilePictureUrl { get; init; }
}
