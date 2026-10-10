using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

// Link token from an email (verify email, confirm email change)
public record EmailLinkTokenDto
{
    [Required, StringLength(100)]
    public string Token { get; init; } = string.Empty;
}

public record CurrentPasswordDto
{
    [Required, StringLength(20)]
    public string CurrentPassword { get; init; } = string.Empty;
}

public record TwoFactorCodeDto
{
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; init; } = string.Empty;
}

public record TwoFactorLoginDto
{
    [Required, StringLength(2000)]
    public string ChallengeToken { get; init; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; init; } = string.Empty;
}

public record TwoFactorResendDto
{
    [Required, StringLength(2000)]
    public string ChallengeToken { get; init; } = string.Empty;
}

public record SecurityStatusDto
{
    public string Email { get; init; } = string.Empty;
    public bool IsEmailConfirmed { get; init; }
    public bool IsTwoFactorEnabled { get; init; }
    public string? PendingEmail { get; init; }
}
