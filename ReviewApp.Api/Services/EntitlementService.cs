using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Reads the tier from the stored subscription on every call and applies <see cref="SubscriptionRules"/>, so the
/// answer reflects time passing (a period ending) without any job having to update a flag.
/// </summary>
public class EntitlementService : IEntitlementService
{
    private readonly AppDbContext _context;

    public EntitlementService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionTier> GetTierAsync(int userId)
    {
        // No row means Free
        var subscription = await _context.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserID == userId);
        return SubscriptionRules.GetTier(subscription.ToState(), DateTime.UtcNow);
    }

    public async Task<bool> HasFeatureAsync(int userId, string featureKey) =>
        Entitlements.IsAvailable(featureKey, await GetTierAsync(userId));
}
