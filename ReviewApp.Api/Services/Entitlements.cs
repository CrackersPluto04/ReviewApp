using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// Stable keys of the features that depend on the subscription tier. The clients receive the keys
// the user is entitled to, so renaming one is a breaking change.
public static class FeatureKeys
{
    public const string ProfileBadge = "profile-badge";
    public const string CameraRecognition = "camera-recognition";
}

public record PremiumFeature(string Key, string Title, string Description, SubscriptionTier MinimumTier, bool ComingSoon = false);

// The single place that says which tier a feature needs. Adding a Premium feature = a key + an entry here
// + [RequiresFeature] on its endpoints + the upsell on the clients.
public static class Entitlements
{
    public static readonly IReadOnlyList<PremiumFeature> Catalog =
    [
        new(FeatureKeys.ProfileBadge, "Premium badge", "A badge on your profile that everyone can see.", SubscriptionTier.Premium),
        new(FeatureKeys.CameraRecognition, "Camera recognition",
            "Take a photo of a figure, poster, album cover or other collectible and get the movies, series and music it relates to.",
            SubscriptionTier.Premium),
        // Announced on the plans page, not enforced anywhere yet
        new("offline-mode", "Offline mode", "Use the mobile app without an internet connection.", SubscriptionTier.Premium, ComingSoon: true)
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
