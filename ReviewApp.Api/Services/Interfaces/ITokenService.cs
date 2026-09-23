using ReviewApp.Api.DAL.Entities;

namespace ReviewApp.Api.Services.Interfaces;

public interface ITokenService
{
    // Creates a JWT for the user and sets it as the httpOnly auth cookie on the current response
    void IssueAuthCookie(User user);
}
