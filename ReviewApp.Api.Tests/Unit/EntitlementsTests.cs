using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

// Complements SubscriptionServiceTests with the catalog-wide rules
public class EntitlementsTests
{
    [Fact]
    public void Catalog_KeysAreUnique()
    {
        Entitlements.Catalog.Select(f => f.Key).Distinct().Count().ShouldBe(Entitlements.Catalog.Count);
    }

    [Fact]
    public void EveryFeatureKeyConstant_IsInTheCatalog()
    {
        var constants = typeof(FeatureKeys).GetFields().Select(f => (string)f.GetValue(null)!);

        constants.ShouldAllBe(key => Entitlements.Catalog.Any(f => f.Key == key));
    }

    [Fact]
    public void ComingSoonFeatures_AreNeverGranted()
    {
        foreach (var feature in Entitlements.Catalog.Where(f => f.ComingSoon))
        {
            Entitlements.IsAvailable(feature.Key, SubscriptionTier.Premium).ShouldBeFalse();
            Entitlements.FeaturesFor(SubscriptionTier.Premium).ShouldNotContain(feature.Key);
        }
    }

    [Fact]
    public void Free_GetsNoPremiumFeature()
    {
        Entitlements.FeaturesFor(SubscriptionTier.Free).ShouldBeEmpty();
        Entitlements.IsAvailable(FeatureKeys.ProfileBadge, SubscriptionTier.Free).ShouldBeFalse();
    }

    [Fact]
    public void Premium_GetsEveryEnforcedFeature()
    {
        Entitlements.FeaturesFor(SubscriptionTier.Premium)
            .ShouldBe(Entitlements.Catalog.Where(f => !f.ComingSoon).Select(f => f.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PROFILE-BADGE")]
    [InlineData("no-such-feature")]
    public void UnknownKeys_AreNeverGranted(string key)
    {
        Entitlements.IsAvailable(key, SubscriptionTier.Premium).ShouldBeFalse();
    }
}
