using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// The one-time token from an emailed link (verify email, confirm email change).
/// </summary>
public record EmailLinkTokenDto
{
    [Required, StringLength(100)]
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// The caller's current password, to re-authenticate a sensitive change. Capped at 20 characters like every
/// password, which keeps it under BCrypt's 72-byte input limit even with multi-byte characters.
/// </summary>
public record CurrentPasswordDto
{
    [Required, StringLength(20)]
    public string CurrentPassword { get; init; } = string.Empty;
}

/// <summary>
/// The emailed code that confirms enabling or disabling two-factor authentication.
/// </summary>
public record TwoFactorCodeDto
{
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Second step of a two-factor login: the challenge from the password step plus the emailed code.
/// </summary>
public record TwoFactorLoginDto
{
    /// <summary>The short-lived signed challenge returned by login. It is not a session and the bearer handler never accepts it as one.</summary>
    [Required, StringLength(2000)]
    public string ChallengeToken { get; init; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Request to email a new login code for a pending two-factor login.
/// </summary>
public record TwoFactorResendDto
{
    [Required, StringLength(2000)]
    public string ChallengeToken { get; init; } = string.Empty;
}

/// <summary>
/// The caller's security state. The only place the clients read the email and the pending address from.
/// </summary>
public record SecurityStatusDto
{
    public string Email { get; init; } = string.Empty;
    public bool IsEmailConfirmed { get; init; }
    public bool IsTwoFactorEnabled { get; init; }
    /// <summary>The new address of an unconfirmed email change; null when none is pending.</summary>
    public string? PendingEmail { get; init; }
}
