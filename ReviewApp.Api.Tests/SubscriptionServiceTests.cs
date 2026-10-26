using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests;

// The pure parts of SubscriptionService and the entitlement map
public class SubscriptionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildReturnUrl_Web_PointsToFrontendReturnPage()
    {
        var url = SubscriptionService.BuildReturnUrl("https://localhost:5173/", "http://localhost:5062", false, "success");

        Assert.Equal("https://localhost:5173/subscription/return?status=success", url);
    }

    [Fact]
    public void BuildReturnUrl_Android_PointsToApiPageThatOpensTheApp()
    {
        var url = SubscriptionService.BuildReturnUrl("https://localhost:5173", "http://localhost:5062/", true, "cancelled");

        Assert.Equal("http://localhost:5062/api/Subscription/return/android?result=cancelled", url);
    }

    [Fact]
    public void ToDto_NoSubscription_IsFree()
    {
        var dto = SubscriptionService.ToDto(SubscriptionState.Empty, Now);

        Assert.Equal("Free", dto.Tier);
        Assert.Equal("None", dto.Status);
        Assert.False(dto.IsPremium);
        Assert.False(dto.WillRenew);
        Assert.Null(dto.AccessEndsAt);
    }

    [Fact]
    public void ToDto_Active_RenewsAtPeriodEnd()
    {
        var end = Now.AddDays(20);
        var dto = SubscriptionService.ToDto(new SubscriptionState(SubscriptionStatus.Active, false, end, null), Now);

        Assert.Equal("Premium", dto.Tier);
        Assert.True(dto.IsPremium);
        Assert.True(dto.WillRenew);
        Assert.False(dto.CancelAtPeriodEnd);
        Assert.Equal(end, dto.AccessEndsAt);
    }

    [Fact]
    public void ToDto_PastDue_ShowsPaymentProblemAndGraceEnd()
    {
        var since = Now.AddDays(-1);
        var dto = SubscriptionService.ToDto(new SubscriptionState(SubscriptionStatus.PastDue, false, since, since), Now);

        Assert.True(dto.IsPremium);
        Assert.True(dto.HasPaymentProblem);
        Assert.False(dto.WillRenew);
        Assert.Equal(since + SubscriptionRules.PastDueGrace, dto.AccessEndsAt);
    }

    [Fact]
    public void ToDto_LapsedButNotYetExpired_IsShownAsEnded()
    {
        var dto = SubscriptionService.ToDto(new SubscriptionState(SubscriptionStatus.Active, true, Now.AddDays(-1), null), Now);

        Assert.Equal("Free", dto.Tier);
        Assert.Equal("Ended", dto.Status);
        Assert.False(dto.CancelAtPeriodEnd);
        Assert.Null(dto.AccessEndsAt);
    }

    [Fact]
    public void Entitlements_PremiumFeaturesOnlyForPremium()
    {
        Assert.True(Entitlements.IsAvailable(FeatureKeys.ProfileBadge, SubscriptionTier.Premium));
        Assert.False(Entitlements.IsAvailable(FeatureKeys.ProfileBadge, SubscriptionTier.Free));
        Assert.Empty(Entitlements.FeaturesFor(SubscriptionTier.Free));
        Assert.Contains(FeatureKeys.EntitlementPreview, Entitlements.FeaturesFor(SubscriptionTier.Premium));
    }

    [Fact]
    public void Entitlements_UnknownAndComingSoonFeaturesAreNeverGranted()
    {
        Assert.False(Entitlements.IsAvailable("no-such-feature", SubscriptionTier.Premium));
        Assert.False(Entitlements.IsAvailable("offline-mode", SubscriptionTier.Premium));
        Assert.DoesNotContain("offline-mode", Entitlements.FeaturesFor(SubscriptionTier.Premium));
    }
}
