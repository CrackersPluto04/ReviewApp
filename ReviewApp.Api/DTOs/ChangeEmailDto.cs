using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Request to change the email address. The address only changes once the link mailed to the new one is confirmed.
/// </summary>
public record ChangeEmailDto
{
    [Required, EmailAddress, StringLength(254)]
    public string NewEmail { get; init; } = string.Empty;

    /// <summary>Re-authenticates the change.</summary>
    [Required, StringLength(20)]
    public string CurrentPassword { get; init; } = string.Empty;
}
