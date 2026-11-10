using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Reads the user ID from the <c>id</c> claim of the current request's principal. The claim is trusted because the
/// JWT bearer handler has already validated the token and its <c>TokenVersion</c>.
/// </summary>
public class UserAuthHelper : IUserAuthHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserAuthHelper(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int GetSecureUserID()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("id");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
        }

        return userId;
    }

    public int? GetOptionalUserID()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("id");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return null;
        }

        return userId;
    }
}
