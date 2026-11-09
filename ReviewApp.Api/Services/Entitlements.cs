using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// Stable keys of the features that depend on the subscription tier. The clients receive the keys
// the user is entitled to, so renaming one is a breaking change.
public static class FeatureKeys
{
    public const string ProfileBadge = "profile-badge";
    public const string CameraRecognition = "camera-recognition";
    public const string OfflineMode = "offline-mode";
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
        // Decided on the device (the sync endpoints stay open, so changes made offline arrive after Premium lapses)
        new(FeatureKeys.OfflineMode, "Offline mode",
            "Keep using the Android app without an internet connection: your reviews and collections are kept on the phone, and your changes sync when you are back online.",
            SubscriptionTier.Premium)
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
