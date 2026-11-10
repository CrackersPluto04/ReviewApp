namespace ReviewApp.Api.Enums;

/// <summary>
/// What a <c>SecurityToken</c> may be used for. Every type is single purpose: a token or code only works for the
/// purpose it was issued for, so a login code cannot confirm disabling two-factor authentication.
/// </summary>
public enum SecurityTokenType
{
    EmailConfirmation,
    EmailChange,
    /// <summary>Reserved for a later forgot-password flow.</summary>
    PasswordChange,
    TwoFactorLogin,
    TwoFactorEnable,
    TwoFactorDisable
}
