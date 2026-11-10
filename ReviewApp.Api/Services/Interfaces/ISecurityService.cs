using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Email verification, email change confirmation and email based two-factor authentication. Link tokens and codes
/// are single purpose and stored only as hashes. Issuing one invalidates the user's older unused ones of the same
/// kind, and consuming one is atomic.
/// </summary>
public interface ISecurityService
{
    /// <summary>Sends the welcome / verification email and creates the welcome notification of a freshly saved user.</summary>
    Task OnRegisteredAsync(User user);

    /// <summary>Returns the user's email, verification state, pending email change and two-factor state.</summary>
    /// <returns>The status, or null if the user does not exist.</returns>
    Task<SecurityStatusDto?> GetStatusAsync(int userId);

    /// <summary>Confirms an email address from the token in a verification link.</summary>
    /// <param name="token">The raw token from the link.</param>
    Task<(bool Success, string Message)> ConfirmEmailAsync(string token);

    /// <summary>Sends a new verification link, subject to a 60 second cooldown.</summary>
    Task<(bool Success, string Message)> ResendVerificationAsync(int userId);

    /// <summary>
    /// Starts an email change: checks the password and the current address's verification, then mails a
    /// confirmation link to the new address and a notice to the old one. The email only changes once that link
    /// is confirmed.
    /// </summary>
    Task<(bool Success, string Message)> RequestEmailChangeAsync(int userId, ChangeEmailDto dto);

    /// <summary>
    /// Completes an email change from the emailed token: sets the new address, marks it verified and revokes every
    /// session of the user by bumping <c>TokenVersion</c>.
    /// </summary>
    /// <returns>Success, a message, and the updated user (so the caller can re-issue a session for the browser that confirmed).</returns>
    Task<(bool Success, string Message, User? User)> ConfirmEmailChangeAsync(string token);

    /// <summary>Cancels a pending email change.</summary>
    /// <returns>False when none is pending.</returns>
    Task<bool> CancelEmailChangeAsync(int userId);

    /// <summary>
    /// First step of enabling or disabling two-factor authentication: checks the password and emails a code. Needs
    /// a verified email.
    /// </summary>
    /// <param name="userId">The user changing their setting.</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <param name="currentPassword">The user's current password, checked before any code is sent.</param>
    Task<(bool Success, string Message)> RequestTwoFactorChangeAsync(int userId, bool enable, string currentPassword);

    /// <summary>Second step: checks the emailed code. Codes burn after 5 wrong tries.</summary>
    /// <param name="userId">The user changing their setting.</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <param name="code">The six-digit code from the email.</param>
    Task<(bool Success, string Message)> ConfirmTwoFactorChangeAsync(int userId, bool enable, string code);

    /// <summary>Starts a two-factor login after the password was verified: emails a code.</summary>
    /// <returns>The challenge token the second step needs. It is a short-lived JWT, not a session.</returns>
    Task<string> StartTwoFactorLoginAsync(User user);

    /// <summary>Completes a two-factor login by checking the challenge and the code.</summary>
    /// <returns>Success, a message, and the user to issue the session for.</returns>
    Task<(bool Success, string Message, User? User)> CompleteTwoFactorLoginAsync(string challengeToken, string code);

    /// <summary>Emails a new login code for a pending two-factor login, subject to a 60 second cooldown.</summary>
    Task<(bool Success, string Message)> ResendTwoFactorLoginCodeAsync(string challengeToken);
}
