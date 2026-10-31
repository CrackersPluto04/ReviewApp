using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// Stable keys of the features that depend on the subscription tier. The clients receive the keys
// the user is entitled to, so renaming one is a breaking change.
public static class FeatureKeys
{
    public const string ProfileBadge = "profile-badge";

    // Placeholder that only proves the paywall works end to end (GET api/Subscription/entitlement-preview).
    // Remove it once the first real Premium feature exists.
    public const string EntitlementPreview = "entitlement-preview";
}

public record PremiumFeature(string Key, string Title, string Description, SubscriptionTier MinimumTier, bool ComingSoon = false);

// The single place that says which tier a feature needs. Adding a Premium feature = a key + an entry here
// + [RequiresFeature] on its endpoints + the upsell on the clients.
public static class Entitlements
{
    public static readonly IReadOnlyList<PremiumFeature> Catalog =
    [
        new(FeatureKeys.ProfileBadge, "Premium badge", "A badge on your profile that everyone can see.", SubscriptionTier.Premium),
        new(FeatureKeys.EntitlementPreview, "Early access", "Try new Premium features before anyone else.", SubscriptionTier.Premium),
        // Announced on the plans page, not enforced anywhere yet
        new("offline-mode", "Offline mode", "Use the mobile app without an internet connection.", SubscriptionTier.Premium, ComingSoon: true),
        new("camera-recognition", "Camera recognition", "Point your camera at a movie poster or album cover to find it.", SubscriptionTier.Premium, ComingSoon: true)
    ];

    public static bool IsAvailable(string featureKey, SubscriptionTier tier)
    {
        var feature = Catalog.FirstOrDefault(f => f.Key == featureKey && !f.ComingSoon);
        // Unknown keys are never granted, so a typo can't open a feature to everyone
        return feature != null && tier >= feature.MinimumTier;
    }

    public static IReadOnlyList<string> FeaturesFor(SubscriptionTier tier) =>
        Catalog.Where(f => !f.ComingSoon && tier >= f.MinimumTier).Select(f => f.Key).ToList();
}
