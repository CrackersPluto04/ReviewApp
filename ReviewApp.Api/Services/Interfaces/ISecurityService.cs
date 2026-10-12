using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

// Email verification, email change confirmation and email based two-factor authentication
public interface ISecurityService
{
    // Sends the welcome / verification email and the welcome notification of a freshly saved user
    Task OnRegisteredAsync(User user);

    Task<SecurityStatusDto?> GetStatusAsync(int userId);

    Task<(bool Success, string Message)> ConfirmEmailAsync(string token);
    Task<(bool Success, string Message)> ResendVerificationAsync(int userId);

    // The email only changes once the link sent to the new address is confirmed
    Task<(bool Success, string Message)> RequestEmailChangeAsync(int userId, ChangeEmailDto dto);
    // Returns the updated user, its sessions are revoked
    Task<(bool Success, string Message, User? User)> ConfirmEmailChangeAsync(string token);
    Task<bool> CancelEmailChangeAsync(int userId);

    // Enabling / disabling: the password is checked when the code is requested, the code when it's confirmed
    Task<(bool Success, string Message)> RequestTwoFactorChangeAsync(int userId, bool enable, string currentPassword);
    Task<(bool Success, string Message)> ConfirmTwoFactorChangeAsync(int userId, bool enable, string code);

    // Login of a user with 2FA: sends a code and returns the challenge token the second step needs
    Task<string> StartTwoFactorLoginAsync(User user);
    Task<(bool Success, string Message, User? User)> CompleteTwoFactorLoginAsync(string challengeToken, string code);
    Task<(bool Success, string Message)> ResendTwoFactorLoginCodeAsync(string challengeToken);
}
