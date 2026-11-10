using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Answers "may this user use feature X?". Always read from the database per request and never from the login
/// token, so subscribing or expiring takes effect on the next request.
/// </summary>
public interface IEntitlementService
{
    /// <summary>The tier the user can use right now.</summary>
    Task<SubscriptionTier> GetTierAsync(int userId);

    /// <summary>Whether the user's tier grants a feature.</summary>
    /// <param name="userId">The user to check.</param>
    /// <param name="featureKey">A key from <c>FeatureKeys</c>.</param>
    Task<bool> HasFeatureAsync(int userId, string featureKey);
}
