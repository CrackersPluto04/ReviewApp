using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class SecurityService : ISecurityService
{
    // Expired, used, unknown and wrong values all get the same answer
    private const string InvalidCodeMessage = "The code is invalid or has expired.";
    private const string LoginExpiredMessage = "Your login attempt has expired. Please log in again.";
    private const string CooldownMessage = "Please wait a minute before requesting another email.";

    private readonly AppDbContext _context;
    private readonly IAccountEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<SecurityService> _logger;

    public SecurityService(AppDbContext context, IAccountEmailService emailService, INotificationService notificationService,
        ITokenService tokenService, ILogger<SecurityService> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task OnRegisteredAsync(User user)
    {
        try
        {
            var token = SecurityTokens.GenerateLinkToken();
            await IssueTokenAsync(user.ID, SecurityTokenType.EmailConfirmation, token, SecurityTokens.EmailVerificationLifetime);
            _emailService.SendWelcome(user.Email, user.Username, token);
        }
        catch (Exception ex)
        {
            // The account exists already, verification can be resent from the app
            _logger.LogError(ex, "Sending the welcome email to user {UserId} failed", user.ID);
        }

        await _notificationService.CreateSystemAsync(user.ID, "Welcome to THE Review App!",
            "Your account is ready. Check your inbox and verify your email address to unlock every account feature.");
    }

    public async Task<SecurityStatusDto?> GetStatusAsync(int userId) =>
        await _context.Users
            .Where(u => u.ID == userId)
            .Select(u => new SecurityStatusDto
            {
                Email = u.Email,
                IsEmailConfirmed = u.IsEmailConfirmed,
                IsTwoFactorEnabled = u.IsTwoFactorEnabled,
                PendingEmail = u.PendingEmail
            })
            .FirstOrDefaultAsync();

    /* Email verification */

    public async Task<(bool Success, string Message)> ConfirmEmailAsync(string token)
    {
        const string invalidMessage = "This verification link is invalid or has expired.";

        var hash = SecurityTokens.Hash(token);
        var row = await _context.SecurityTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Type == SecurityTokenType.EmailConfirmation);

        if (row == null)
            return (false, invalidMessage);

        // Opening the link again is harmless
        if (row.User.IsEmailConfirmed)
            return (true, "Your email address is already verified.");

        if (row.IsUsed || row.ExpiresAt <= DateTime.UtcNow || !await TryConsumeAsync(row.ID))
            return (false, invalidMessage);

        row.User.IsEmailConfirmed = true;
        await _context.SaveChangesAsync();

        await _notificationService.CreateSystemAsync(row.UserID, "Email verified", "Your email address is verified. Thanks!");
        return (true, "Your email address is verified. Thanks!");
    }

    public async Task<(bool Success, string Message)> ResendVerificationAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        if (user.IsEmailConfirmed)
            return (false, "Your email address is already verified.");

        if (await IsInCooldownAsync(userId, SecurityTokenType.EmailConfirmation))
            return (false, CooldownMessage);

        var token = SecurityTokens.GenerateLinkToken();
        await IssueTokenAsync(userId, SecurityTokenType.EmailConfirmation, token, SecurityTokens.EmailVerificationLifetime);
        _emailService.SendVerification(user.Email, user.Username, token);

        return (true, $"We sent a new verification link to {user.Email}.");
    }

    /* Email change */

    public async Task<(bool Success, string Message)> RequestEmailChangeAsync(int userId, ChangeEmailDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        // Password is verified before anything else so this can't be used to probe which emails are registered
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return (false, "Incorrect password.");

        // The notice about the change goes to the current address, so it has to be a real one
        if (!user.IsEmailConfirmed)
            return (false, "Please verify your current email address before changing it.");

        var newEmail = dto.NewEmail.Trim();
        if (newEmail == user.Email)
            return (false, "This is already your email address.");

        // Exclude the user's own row so a case-only change is allowed
        if (await _context.Users.AnyAsync(u => u.Email == newEmail && u.ID != userId))
            return (false, "Email already in use.");

        // A new request replaces the previous one (IssueTokenAsync invalidates the older link)
        user.PendingEmail = newEmail;

        var token = SecurityTokens.GenerateLinkToken();
        await IssueTokenAsync(userId, SecurityTokenType.EmailChange, token, SecurityTokens.EmailChangeLifetime);

        _emailService.SendEmailChangeConfirmation(newEmail, user.Username, token);
        _emailService.SendEmailChangeRequestedNotice(user.Email, user.Username, newEmail);

        return (true, $"We sent a confirmation link to {newEmail}. Your email changes once you open it.");
    }

    public async Task<(bool Success, string Message, User? User)> ConfirmEmailChangeAsync(string token)
    {
        const string invalidMessage = "This confirmation link is invalid or has expired.";

        var hash = SecurityTokens.Hash(token);
        var row = await _context.SecurityTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Type == SecurityTokenType.EmailChange);

        if (row == null || row.IsUsed || row.ExpiresAt <= DateTime.UtcNow || row.User.PendingEmail == null)
            return (false, invalidMessage, null);

        var user = row.User;
        var newEmail = user.PendingEmail;

        // Someone may have registered or confirmed this address since the request
        if (await _context.Users.AnyAsync(u => u.Email == newEmail && u.ID != user.ID))
            return (false, "This email address is already used by another account.", null);

        if (!await TryConsumeAsync(row.ID))
            return (false, invalidMessage, null);

        var oldEmail = user.Email;
        user.Email = newEmail;
        user.PendingEmail = null;
        // The link proves the new address is reachable
        user.IsEmailConfirmed = true;
        // The email is a recovery factor: log out every session
        user.TokenVersion++;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index violation from a concurrent request taking the same email
            return (false, "This email address is already used by another account.", null);
        }

        // Verification links sent to the old address must not verify the new one
        await InvalidateTokensAsync(user.ID, SecurityTokenType.EmailConfirmation);

        await _notificationService.CreateSystemAsync(user.ID, "Email address changed",
            $"Your account's email address was changed from {oldEmail} to {newEmail}.");

        return (true, "Your email address has been changed.", user);
    }

    public async Task<bool> CancelEmailChangeAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.PendingEmail == null)
            return false;

        user.PendingEmail = null;
        await _context.SaveChangesAsync();
        await InvalidateTokensAsync(userId, SecurityTokenType.EmailChange);

        return true;
    }

    /* Two-factor authentication settings */

    public async Task<(bool Success, string Message)> RequestTwoFactorChangeAsync(int userId, bool enable, string currentPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return (false, "Incorrect password.");

        var stateError = CheckTwoFactorState(user, enable);
        if (stateError != null)
            return (false, stateError);

        var type = enable ? SecurityTokenType.TwoFactorEnable : SecurityTokenType.TwoFactorDisable;
        if (await IsInCooldownAsync(userId, type))
            return (false, "Please wait a minute before requesting another code.");

        var code = SecurityTokens.GenerateCode();
        await IssueTokenAsync(userId, type, code, SecurityTokens.TwoFactorCodeLifetime);
        _emailService.SendTwoFactorCode(user.Email, user.Username, code, enable ? TwoFactorCodePurpose.Enable : TwoFactorCodePurpose.Disable);

        return (true, $"We sent a 6-digit code to {user.Email}.");
    }

    public async Task<(bool Success, string Message)> ConfirmTwoFactorChangeAsync(int userId, bool enable, string code)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        var stateError = CheckTwoFactorState(user, enable);
        if (stateError != null)
            return (false, stateError);

        var type = enable ? SecurityTokenType.TwoFactorEnable : SecurityTokenType.TwoFactorDisable;
        if (!await VerifyCodeAsync(userId, type, code))
            return (false, InvalidCodeMessage);

        user.IsTwoFactorEnabled = enable;
        await _context.SaveChangesAsync();

        _emailService.SendTwoFactorStatusChanged(user.Email, user.Username, enable);
        await _notificationService.CreateSystemAsync(userId,
            enable ? "Two-factor authentication enabled" : "Two-factor authentication disabled",
            enable
                ? "Logging in now requires a code sent to your email address."
                : "Logging in no longer requires an emailed code.");

        return (true, enable ? "Two-factor authentication is now enabled." : "Two-factor authentication is now disabled.");
    }

    private static string? CheckTwoFactorState(User user, bool enable)
    {
        if (enable && user.IsTwoFactorEnabled)
            return "Two-factor authentication is already enabled.";

        // Codes go to the email address, an unverified one could lock the user out
        if (enable && !user.IsEmailConfirmed)
            return "Please verify your email address before enabling two-factor authentication.";

        if (!enable && !user.IsTwoFactorEnabled)
            return "Two-factor authentication is not enabled.";

        return null;
    }

    /* Two-factor login */

    public async Task<string> StartTwoFactorLoginAsync(User user)
    {
        var code = SecurityTokens.GenerateCode();
        await IssueTokenAsync(user.ID, SecurityTokenType.TwoFactorLogin, code, SecurityTokens.TwoFactorCodeLifetime);
        _emailService.SendTwoFactorCode(user.Email, user.Username, code, TwoFactorCodePurpose.Login);

        return _tokenService.CreateTwoFactorChallenge(user);
    }

    public async Task<(bool Success, string Message, User? User)> CompleteTwoFactorLoginAsync(string challengeToken, string code)
    {
        var user = await GetChallengeUserAsync(challengeToken);
        if (user == null)
            return (false, LoginExpiredMessage, null);

        if (!await VerifyCodeAsync(user.ID, SecurityTokenType.TwoFactorLogin, code))
            return (false, InvalidCodeMessage, null);

        return (true, "Logged in successfully", user);
    }

    public async Task<(bool Success, string Message)> ResendTwoFactorLoginCodeAsync(string challengeToken)
    {
        var user = await GetChallengeUserAsync(challengeToken);
        if (user == null)
            return (false, LoginExpiredMessage);

        if (await IsInCooldownAsync(user.ID, SecurityTokenType.TwoFactorLogin))
            return (false, "Please wait a minute before requesting another code.");

        var code = SecurityTokens.GenerateCode();
        await IssueTokenAsync(user.ID, SecurityTokenType.TwoFactorLogin, code, SecurityTokens.TwoFactorCodeLifetime);
        _emailService.SendTwoFactorCode(user.Email, user.Username, code, TwoFactorCodePurpose.Login);

        return (true, "We sent a new code to your email address.");
    }

    // The challenge is only valid while the sessions it was issued for are (same token version) and 2FA is still on
    private async Task<User?> GetChallengeUserAsync(string challengeToken)
    {
        var challenge = _tokenService.ValidateTwoFactorChallenge(challengeToken);
        if (challenge == null)
            return null;

        var user = await _context.Users.FindAsync(challenge.Value.UserId);
        if (user == null || user.TokenVersion != challenge.Value.TokenVersion || !user.IsTwoFactorEnabled)
            return null;

        return user;
    }

    /* Token helpers */

    // Only the newest token of a type works: older unused ones are invalidated
    private async Task IssueTokenAsync(int userId, SecurityTokenType type, string rawValue, TimeSpan lifetime)
    {
        await InvalidateTokensAsync(userId, type);

        var now = DateTime.UtcNow;
        _context.SecurityTokens.Add(new SecurityToken
        {
            UserID = userId,
            Type = type,
            TokenHash = SecurityTokens.Hash(rawValue),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime)
        });

        await _context.SaveChangesAsync();
    }

    private Task InvalidateTokensAsync(int userId, SecurityTokenType type) =>
        _context.SecurityTokens
            .Where(t => t.UserID == userId && t.Type == type && !t.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true));

    private Task<bool> IsInCooldownAsync(int userId, SecurityTokenType type)
    {
        var since = DateTime.UtcNow - SecurityTokens.ResendCooldown;
        return _context.SecurityTokens.AnyAsync(t => t.UserID == userId && t.Type == type && t.CreatedAt > since);
    }

    // Atomic, so two concurrent requests can't both use the same token
    private async Task<bool> TryConsumeAsync(int tokenId) =>
        await _context.SecurityTokens
            .Where(t => t.ID == tokenId && !t.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true)) == 1;

    private async Task<bool> VerifyCodeAsync(int userId, SecurityTokenType type, string code)
    {
        var now = DateTime.UtcNow;
        var token = await _context.SecurityTokens
            .AsNoTracking()
            .Where(t => t.UserID == userId && t.Type == type && !t.IsUsed && t.ExpiresAt > now)
            .OrderByDescending(t => t.ID)
            .FirstOrDefaultAsync();

        if (token == null)
            return false;

        if (!SecurityTokens.Matches(code, token.TokenHash))
        {
            // Too many wrong tries burn the code, the user has to request a new one.
            // Only while it's unused: the right code may have consumed it since it was read, and that must stay.
            await _context.SecurityTokens
                .Where(t => t.ID == token.ID && !t.IsUsed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.FailedAttempts, t => t.FailedAttempts + 1)
                    .SetProperty(t => t.IsUsed, t => t.FailedAttempts + 1 >= SecurityTokens.MaxCodeAttempts));
            return false;
        }

        return await TryConsumeAsync(token.ID);
    }
}
