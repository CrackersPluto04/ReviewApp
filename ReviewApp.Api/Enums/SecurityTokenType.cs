namespace ReviewApp.Api.Enums;

public enum SecurityTokenType
{
    EmailConfirmation,
    EmailChange,
    PasswordChange, // reserved for a later forgot-password flow
    // Each 2FA code only works for the purpose it was sent for
    TwoFactorLogin,
    TwoFactorEnable,
    TwoFactorDisable
}
