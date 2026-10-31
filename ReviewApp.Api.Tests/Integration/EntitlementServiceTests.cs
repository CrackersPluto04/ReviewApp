using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The tier comes from the stored subscription on every call, never from anything cached in the session
public class EntitlementServiceTests : IntegrationTest
{
    private IEntitlementService Entitlements => Service<IEntitlementService>();

    public static TheoryData<SubscriptionStatus, int, int?, bool, SubscriptionTier> States => new()
    {
        // status, period end (days from now), past due since (days from now), cancelled, expected tier
        { SubscriptionStatus.Active, 10, null, false, SubscriptionTier.Premium },
        { SubscriptionStatus.Active, 10, null, true, SubscriptionTier.Premium },
        { SubscriptionStatus.Active, -1, null, false, SubscriptionTier.Free },
        { SubscriptionStatus.PastDue, -1, -2, false, SubscriptionTier.Premium },
        { SubscriptionStatus.PastDue, -1, -6, false, SubscriptionTier.Free },
        { SubscriptionStatus.Ended, 10, null, false, SubscriptionTier.Free },
        { SubscriptionStatus.None, 10, null, false, SubscriptionTier.Free }
    };

    [Theory]
    [MemberData(nameof(States))]
    public async Task Tier_FollowsTheStoredSubscription(SubscriptionStatus status, int periodEndDays, int? pastDueDays, bool cancelled, SubscriptionTier expected)
    {
        var user = await Data.UserAsync();
        var now = DateTime.UtcNow;
        await Data.SubscriptionAsync(user, status, now.AddDays(periodEndDays), cancelled,
            pastDueDays is { } days ? now.AddDays(days) : null);

        (await Entitlements.GetTierAsync(user.ID)).ShouldBe(expected);
        (await Entitlements.HasFeatureAsync(user.ID, FeatureKeys.ProfileBadge)).ShouldBe(expected == SubscriptionTier.Premium);
    }

    [Fact]
    public async Task NoSubscription_IsFreeWithoutAnyFeature()
    {
        var user = await Data.UserAsync();

        (await Entitlements.GetTierAsync(user.ID)).ShouldBe(SubscriptionTier.Free);
        (await Entitlements.HasFeatureAsync(user.ID, FeatureKeys.ProfileBadge)).ShouldBeFalse();
    }

    [Fact]
    public async Task ComingSoonFeature_IsNotGrantedToPremium()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);

        (await Entitlements.HasFeatureAsync(user.ID, "offline-mode")).ShouldBeFalse();
    }
}
