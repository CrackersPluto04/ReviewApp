using ReviewApp.Api.DAL.Entities;

namespace ReviewApp.Api.Services.Interfaces;

public interface ITokenService
{
    // Creates a JWT for the user and sets it as the httpOnly auth cookie on the current response
    void IssueAuthCookie(User user);

    // Removes the auth cookie from the current browser (the token itself stays valid until it expires or is revoked)
    void DeleteAuthCookie();

    // Short-lived proof that the password step of a 2FA login succeeded. It has its own audience,
    // so the JWT bearer handler never accepts it as a session token.
    string CreateTwoFactorChallenge(User user);

    // Returns the user ID and token version of a valid challenge, null if it's invalid or expired
    (int UserId, int TokenVersion)? ValidateTwoFactorChallenge(string challenge);
}
