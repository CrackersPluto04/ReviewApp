namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Account-level emails (verification, security notices, subscription changes). Every method only enqueues the
/// message for <c>EmailDispatcherService</c>, so none of them throws or blocks because of the mail server. Queued
/// mail is lost on restart, which is acceptable because every email-dependent flow has a resend.
/// </summary>
public interface IAccountEmailService
{
    /// <summary>Welcomes a new user and carries the first verification link.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="verificationToken">The raw link token; only its hash is stored.</param>
    void SendWelcome(string email, string username, string verificationToken);

    /// <summary>Sends a fresh verification link, for a resend.</summary>
    void SendVerification(string email, string username, string verificationToken);

    /// <summary>Sends the confirmation link to the <em>new</em> address of an email change.</summary>
    /// <param name="newEmail">The address being switched to, which must prove it is reachable.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="changeToken">The raw link token.</param>
    void SendEmailChangeConfirmation(string newEmail, string username, string changeToken);

    /// <summary>Warns the <em>old</em> address that an email change was requested, so a hijacked account is noticed.</summary>
    void SendEmailChangeRequestedNotice(string oldEmail, string username, string newEmail);

    /// <summary>Tells the user their password was changed.</summary>
    void SendPasswordChanged(string email, string username);

    /// <summary>Sends a six-digit two-factor code.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="code">The six-digit code; only its hash is stored.</param>
    /// <param name="purpose">Decides the wording: a login, or enabling or disabling two-factor authentication.</param>
    void SendTwoFactorCode(string email, string username, string code, TwoFactorCodePurpose purpose);

    /// <summary>Tells the user two-factor authentication was switched on or off.</summary>
    void SendTwoFactorStatusChanged(string email, string username, bool enabled);

    /// <summary>Confirms a new Premium subscription.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="renewsOn">Next renewal date, UTC; null when it will not renew.</param>
    void SendPremiumStarted(string email, string username, DateTime? renewsOn);

    /// <summary>Tells the user a payment failed.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="accessEndsAt">When Premium stops if the payment is not fixed, UTC.</param>
    void SendPaymentFailed(string email, string username, DateTime? accessEndsAt);

    /// <summary>Confirms a cancellation. Premium continues until the period end.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="username">Name used in the greeting.</param>
    /// <param name="accessEndsAt">When Premium stops, UTC.</param>
    void SendSubscriptionCancelled(string email, string username, DateTime? accessEndsAt);

    /// <summary>Tells the user Premium has ended.</summary>
    void SendSubscriptionEnded(string email, string username);
}

/// <summary>
/// Why a two-factor code is being sent; decides the wording of the email.
/// </summary>
public enum TwoFactorCodePurpose
{
    Login,
    Enable,
    Disable
}
