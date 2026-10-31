using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

// "May this user use feature X?" Always read from the database per request (never from the login token),
// so subscribing or expiring takes effect on the next request.
public interface IEntitlementService
{
    Task<SubscriptionTier> GetTierAsync(int userId);
    Task<bool> HasFeatureAsync(int userId, string featureKey);
}
