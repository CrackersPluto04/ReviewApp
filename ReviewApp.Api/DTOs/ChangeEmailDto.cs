using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record ChangeEmailDto
{
    [Required, EmailAddress, StringLength(254)]
    public string NewEmail { get; init; } = string.Empty;

    [Required, StringLength(72)]
    public string CurrentPassword { get; init; } = string.Empty;
}
