using ReviewApp.Api.DAL.Entities;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Issues and validates the tokens behind the session cookie and the two-factor login challenge. Always go through
/// this service for the cookie: it owns the cookie name, options and lifetime.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a JWT for the user carrying their ID and <c>TokenVersion</c>, and sets it as the httpOnly auth
    /// cookie on the current response. A token whose version no longer matches the database is rejected, which is
    /// how sessions are revoked.
    /// </summary>
    void IssueAuthCookie(User user);

    /// <summary>
    /// Removes the auth cookie from the current browser. The token itself stays valid until it expires or its
    /// version is revoked, so this only logs out this browser.
    /// </summary>
    void DeleteAuthCookie();

    /// <summary>
    /// Creates a short-lived proof that the password step of a two-factor login succeeded. It has its own
    /// audience, so the JWT bearer handler never accepts it as a session token, and it carries the token version,
    /// so a password change or "log out everywhere" kills pending logins.
    /// </summary>
    string CreateTwoFactorChallenge(User user);

    /// <summary>Validates a two-factor challenge.</summary>
    /// <returns>The user ID and token version of a valid challenge, or null if it is invalid or expired.</returns>
    (int UserId, int TokenVersion)? ValidateTwoFactorChallenge(string challenge);
}
