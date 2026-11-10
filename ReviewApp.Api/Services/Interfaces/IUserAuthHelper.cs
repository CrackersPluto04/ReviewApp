namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Reads the authenticated user's ID from the <c>id</c> claim of the current request.
/// </summary>
public interface IUserAuthHelper
{
    /// <summary>For endpoints that require a login.</summary>
    /// <returns>The user's ID.</returns>
    /// <exception cref="UnauthorizedAccessException">The claim is missing or invalid.</exception>
    int GetSecureUserID();

    /// <summary>For endpoints that work for anonymous visitors too but vary what they return per viewer.</summary>
    /// <returns>The user's ID, or null for an anonymous visitor.</returns>
    int? GetOptionalUserID();
}
