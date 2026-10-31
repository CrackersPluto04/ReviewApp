using System.Net;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// [RequiresFeature] through the pipeline, on the test-only PremiumProbeController
public class PremiumGatingTests : HttpTest
{
    [Fact]
    public async Task FreeUser_Gets402WithTheMachineReadableCode()
    {
        var user = await Data.UserAsync();

        var body = await ExpectAsync(await As(user).GetAsync(PremiumProbeController.Route), HttpStatusCode.PaymentRequired);

        body.Str("error").ShouldBe("This is a Premium feature.");
        body.Str("code").ShouldBe(RequiresFeatureAttribute.PremiumRequiredCode);
        body.Str("feature").ShouldBe(FeatureKeys.ProfileBadge);
    }

    [Fact]
    public async Task AnonymousUser_Gets401NotTheUpsell()
    {
        (await Anonymous().GetAsync(PremiumProbeController.Route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PremiumUser_GetsThrough()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);

        (await ExpectAsync(await As(user).GetAsync(PremiumProbeController.Route), HttpStatusCode.OK)).Str("message").ShouldBe("Premium content");
    }

    [Fact]
    public async Task AccessFollowsTheSubscriptionOnTheNextRequest()
    {
        var user = await Data.UserAsync();
        var subscription = await Data.PremiumAsync(user);
        var client = As(user);
        (await client.GetAsync(PremiumProbeController.Route)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Query().UserSubscriptions.Where(s => s.ID == subscription.ID)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Ended));

        // Same session cookie, no new login: the tier is read per request, not from the token
        (await client.GetAsync(PremiumProbeController.Route)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task CancelledButRunning_StillGetsThrough()
    {
        var user = await Data.UserAsync();
        await Data.SubscriptionAsync(user, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(3), cancelAtPeriodEnd: true);

        (await As(user).GetAsync(PremiumProbeController.Route)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
