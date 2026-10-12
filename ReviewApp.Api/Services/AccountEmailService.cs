using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class AccountEmailService : IAccountEmailService
{
    private const string NotYouHint = "If this wasn't you, change your password right away and log out of all devices from your account settings.";

    private readonly EmailQueue _queue;
    private readonly string _frontendBaseUrl;

    public AccountEmailService(EmailQueue queue, IConfiguration configuration)
    {
        _queue = queue;
        _frontendBaseUrl = configuration["App:FrontendBaseUrl"]!.TrimEnd('/');
    }

    public void SendWelcome(string email, string username, string verificationToken) =>
        Enqueue(email, "Welcome to THE Review App - please verify your email",
            "Welcome aboard!",
            [
                $"Hi {username}, thanks for registering. Your account is ready to use.",
                "Please confirm that this email address belongs to you. A verified address lets you enable two-factor authentication and change your email later."
            ],
            new EmailTemplates.Button("Verify my email", VerificationUrl(verificationToken)),
            footnote: $"The link expires in {SecurityTokens.Describe(SecurityTokens.EmailVerificationLifetime)}. If you didn't create this account, you can ignore this email.");

    public void SendVerification(string email, string username, string verificationToken) =>
        Enqueue(email, "Verify your email address",
            "Verify your email address",
            [$"Hi {username}, please confirm that this email address belongs to you."],
            new EmailTemplates.Button("Verify my email", VerificationUrl(verificationToken)),
            footnote: $"The link expires in {SecurityTokens.Describe(SecurityTokens.EmailVerificationLifetime)}. If you didn't request this, you can ignore this email.");

    public void SendEmailChangeConfirmation(string newEmail, string username, string changeToken) =>
        Enqueue(newEmail, "Confirm your new email address",
            "Confirm your new email address",
            [$"Hi {username}, you asked to use this address for your account. Your email changes once you confirm it."],
            new EmailTemplates.Button("Confirm new email", $"{_frontendBaseUrl}/confirm-email-change?token={Uri.EscapeDataString(changeToken)}"),
            footnote: $"The link expires in {SecurityTokens.Describe(SecurityTokens.EmailChangeLifetime)}. If you didn't request this, you can ignore this email.");

    public void SendEmailChangeRequestedNotice(string oldEmail, string username, string newEmail) =>
        Enqueue(oldEmail, "Your email address is being changed",
            "Email change requested",
            [
                $"Hi {username}, someone asked to change the email address of your account to {newEmail}.",
                "Nothing changes until the link sent to the new address is opened."
            ],
            footnote: $"{NotYouHint} You can also cancel the pending change in your account settings.");

    public void SendPasswordChanged(string email, string username) =>
        Enqueue(email, "Your password was changed",
            "Your password was changed",
            [
                $"Hi {username}, the password of your account was just changed.",
                "All other devices were logged out."
            ],
            footnote: NotYouHint);

    public void SendTwoFactorCode(string email, string username, string code, TwoFactorCodePurpose purpose)
    {
        var (subject, intro) = purpose switch
        {
            TwoFactorCodePurpose.Enable => ("Your code to enable two-factor authentication", "use this code to finish enabling two-factor authentication:"),
            TwoFactorCodePurpose.Disable => ("Your code to disable two-factor authentication", "use this code to confirm disabling two-factor authentication:"),
            _ => ("Your login code", "use this code to finish logging in:")
        };

        Enqueue(email, subject,
            subject,
            [$"Hi {username}, {intro}"],
            code: code,
            footnote: $"The code expires in {SecurityTokens.Describe(SecurityTokens.TwoFactorCodeLifetime)}. Never share it with anyone. {NotYouHint}");
    }

    public void SendTwoFactorStatusChanged(string email, string username, bool enabled) =>
        Enqueue(email, enabled ? "Two-factor authentication enabled" : "Two-factor authentication disabled",
            enabled ? "Two-factor authentication is on" : "Two-factor authentication is off",
            [
                enabled
                    ? $"Hi {username}, from now on logging in to your account requires a code sent to this email address."
                    : $"Hi {username}, logging in to your account no longer requires an emailed code."
            ],
            footnote: NotYouHint);

    private string VerificationUrl(string token) => $"{_frontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";

    private void Enqueue(string to, string subject, string heading, IEnumerable<string> paragraphs,
        EmailTemplates.Button? button = null, string? code = null, string? footnote = null)
    {
        var (html, text) = EmailTemplates.Build(heading, paragraphs, button, code, footnote);
        _queue.Enqueue(new EmailMessage(to, subject, html, text));
    }
}
