using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The subscription lifecycle against the test payment provider (the Fake provider's behaviour plus webhooks)
public class SubscriptionFlowTests : IntegrationTest
{
    private ISubscriptionService Subscriptions => Service<ISubscriptionService>();

    private Task<UserSubscription> RowAsync(User user) => Query().UserSubscriptions.SingleAsync(s => s.UserID == user.ID);

    private Task<List<NotificationType>> NotificationTypesAsync(User user) =>
        Query().Notifications.Where(n => n.RecipientUserID == user.ID).OrderBy(n => n.ID).Select(n => n.Type).ToListAsync();

    // Checkout plus the return page's sync: what a subscribing user goes through
    private async Task<User> SubscribedUserAsync()
    {
        var user = await Data.UserAsync();
        (await Subscriptions.StartCheckoutAsync(user.ID, android: false)).Success.ShouldBeTrue();
        (await Subscriptions.SyncAsync(user.ID)).IsPremium.ShouldBeTrue();
        return user;
    }

    [Fact]
    public async Task Status_WithoutSubscription_IsFree()
    {
        var status = await Subscriptions.GetStatusAsync((await Data.UserAsync()).ID);

        status.Tier.ShouldBe("Free");
        status.Status.ShouldBe("None");
        status.IsPremium.ShouldBeFalse();
    }

    [Fact]
    public async Task Checkout_CreatesTheCustomerAndPointsBackToTheApp()
    {
        var user = await Data.UserAsync();

        var (success, _, url) = await Subscriptions.StartCheckoutAsync(user.ID, android: false);
        var (_, _, androidUrl) = await Subscriptions.StartCheckoutAsync((await Data.UserAsync()).ID, android: true);

        success.ShouldBeTrue();
        url.ShouldBe($"{ApiFactory.FrontendBaseUrl}/subscription/return?status=success");
        androidUrl.ShouldEndWith("/api/Subscription/return/android?result=success");
        var row = await RowAsync(user);
        row.Provider.ShouldBe(SubscriptionProvider.Fake);
        row.ProviderCustomerID.ShouldNotBeNullOrEmpty();
        // Nothing is granted by the redirect itself, only by what the provider reports
        row.Status.ShouldBe(SubscriptionStatus.None);
        (await Subscriptions.GetStatusAsync(user.ID)).IsPremium.ShouldBeFalse();
    }

    [Fact]
    public async Task Sync_AfterPaying_ActivatesAndWelcomesOnce()
    {
        var user = await SubscribedUserAsync();

        await Subscriptions.SyncAsync(user.ID);

        var status = await Subscriptions.GetStatusAsync(user.ID);
        status.Tier.ShouldBe("Premium");
        status.WillRenew.ShouldBeTrue();
        status.AccessEndsAt.ShouldNotBeNull();
        (await NotificationTypesAsync(user)).ShouldBe([NotificationType.SubscriptionStarted]);
        await App.Mail.WaitForAsync(user.Email, "Welcome to Premium");
        (await Service<IEntitlementService>().GetTierAsync(user.ID)).ShouldBe(SubscriptionTier.Premium);
    }

    [Fact]
    public async Task Checkout_WhilePremium_IsRejected()
    {
        var user = await SubscribedUserAsync();

        var (success, message, _) = await Subscriptions.StartCheckoutAsync(user.ID, android: false);

        success.ShouldBeFalse();
        message.ShouldBe("You already have Premium.");
    }

    [Fact]
    public async Task Checkout_WhileCancelledButRunning_SaysToResume()
    {
        var user = await SubscribedUserAsync();
        await Subscriptions.CancelAsync(user.ID);

        (await Subscriptions.StartCheckoutAsync(user.ID, android: false)).Message
            .ShouldBe("Your Premium subscription is still running. Resume it instead of subscribing again.");
    }

    [Fact]
    public async Task Checkout_WithPaymentProblem_SaysToFixPayment()
    {
        var user = await Data.UserAsync();
        await Data.SubscriptionAsync(user, SubscriptionStatus.PastDue, DateTime.UtcNow, pastDueSince: DateTime.UtcNow);

        (await Subscriptions.StartCheckoutAsync(user.ID, android: false)).Message
            .ShouldBe("Your last payment failed. Update your payment details instead of subscribing again.");
    }

    [Fact]
    public async Task Checkout_ProviderDown_FailsWithoutLeavingARow()
    {
        var user = await Data.UserAsync();
        App.Payments.MakeUnavailable(user.ID);

        var (success, message, _) = await Subscriptions.StartCheckoutAsync(user.ID, android: false);

        success.ShouldBeFalse();
        message.ShouldBe("The payment service is unavailable right now. Please try again later.");
        (await Query().UserSubscriptions.AnyAsync(s => s.UserID == user.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task Checkout_AfterTheSubscriptionEnded_ReusesTheRow()
    {
        var user = await Data.UserAsync();
        var ended = await Data.SubscriptionAsync(user, SubscriptionStatus.Ended, DateTime.UtcNow.AddDays(-10));

        (await Subscriptions.StartCheckoutAsync(user.ID, android: false)).Success.ShouldBeTrue();

        (await Query().UserSubscriptions.Where(s => s.UserID == user.ID).Select(s => s.ID).SingleAsync()).ShouldBe(ended.ID);
    }

    [Fact]
    public async Task SecondSubscriptionRow_ForTheSameUser_IsRefusedByTheDatabase()
    {
        var user = await Data.UserAsync();
        await Data.SubscriptionAsync(user, SubscriptionStatus.None);

        await Should.ThrowAsync<DbUpdateException>(() => Data.SubscriptionAsync(user, SubscriptionStatus.None));
    }

    [Fact]
    public async Task Cancel_KeepsPremiumUntilThePeriodEndAndNotifies()
    {
        var user = await SubscribedUserAsync();

        var (success, message) = await Subscriptions.CancelAsync(user.ID);

        success.ShouldBeTrue();
        message.ShouldStartWith("Your subscription is cancelled. You keep Premium until");
        var status = await Subscriptions.GetStatusAsync(user.ID);
        status.IsPremium.ShouldBeTrue();
        status.CancelAtPeriodEnd.ShouldBeTrue();
        status.WillRenew.ShouldBeFalse();
        (await NotificationTypesAsync(user)).ShouldBe([NotificationType.SubscriptionStarted, NotificationType.SubscriptionCancelled]);
        await App.Mail.WaitForAsync(user.Email, "Your Premium subscription was cancelled");
        (await Subscriptions.CancelAsync(user.ID)).Message.ShouldBe("Your subscription is already cancelled.");
    }

    [Fact]
    public async Task Resume_UndoesTheCancellation()
    {
        var user = await SubscribedUserAsync();
        (await Subscriptions.ResumeAsync(user.ID)).Message.ShouldBe("Your subscription is already active.");
        await Subscriptions.CancelAsync(user.ID);

        (await Subscriptions.ResumeAsync(user.ID)).Success.ShouldBeTrue();

        var status = await Subscriptions.GetStatusAsync(user.ID);
        status.WillRenew.ShouldBeTrue();
        status.CancelAtPeriodEnd.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelResumeAndPortal_WithoutSubscription_Fail()
    {
        var user = await Data.UserAsync();

        (await Subscriptions.CancelAsync(user.ID)).Success.ShouldBeFalse();
        (await Subscriptions.ResumeAsync(user.ID)).Success.ShouldBeFalse();
        (await Subscriptions.CreatePortalAsync(user.ID, android: false)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Portal_ReturnsTheProviderPage()
    {
        var user = await SubscribedUserAsync();

        var (success, _, url) = await Subscriptions.CreatePortalAsync(user.ID, android: false);

        success.ShouldBeTrue();
        url.ShouldBe($"{ApiFactory.FrontendBaseUrl}/premium");
    }

    [Fact]
    public async Task Sync_ProviderDown_KeepsTheStoredState()
    {
        var user = await SubscribedUserAsync();
        App.Payments.MakeUnavailable((await RowAsync(user)).ProviderCustomerID);

        (await Subscriptions.SyncAsync(user.ID)).IsPremium.ShouldBeTrue();
    }

    /* Webhooks */

    private Task<bool> WebhookAsync(string eventId, string type, UserSubscription row, string? signature = TestPaymentProvider.ValidSignature) =>
        Subscriptions.HandleWebhookAsync(TestPaymentProvider.WebhookPayload(eventId, type, row.ProviderCustomerID, row.ProviderSubscriptionID), signature);

    [Fact]
    public async Task Webhook_BadSignature_IsRejected()
    {
        var row = await RowAsync(await SubscribedUserAsync());

        (await WebhookAsync(TestData.Unique("evt_"), "invoice.paid", row, signature: "forged")).ShouldBeFalse();
        (await WebhookAsync(TestData.Unique("evt_"), "invoice.paid", row, signature: null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Webhook_IrrelevantType_IsAcknowledgedAndNotStored()
    {
        var row = await RowAsync(await SubscribedUserAsync());
        var eventId = TestData.Unique("evt_");

        (await WebhookAsync(eventId, "customer.created", row)).ShouldBeTrue();

        (await Query().SubscriptionEvents.AnyAsync(e => e.ProviderEventID == eventId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Webhook_AppliesWhatTheProviderReportsNow()
    {
        var user = await SubscribedUserAsync();
        var row = await RowAsync(user);
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.PaymentFailed);
        var eventId = TestData.Unique("evt_");

        (await WebhookAsync(eventId, "invoice.payment_failed", row)).ShouldBeTrue();

        var status = await Subscriptions.GetStatusAsync(user.ID);
        status.HasPaymentProblem.ShouldBeTrue();
        status.IsPremium.ShouldBeTrue(); // grace period
        (await RowAsync(user)).PastDueSince.ShouldNotBeNull();
        (await Query().SubscriptionEvents.SingleAsync(e => e.ProviderEventID == eventId)).ProcessedAt.ShouldNotBeNull();
        await App.Mail.WaitForAsync(user.Email, "We couldn't renew your Premium subscription");
    }

    [Fact]
    public async Task Webhook_RepeatedDelivery_IsAppliedOnce()
    {
        var user = await SubscribedUserAsync();
        var row = await RowAsync(user);
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.PaymentFailed);
        var eventId = TestData.Unique("evt_");

        await WebhookAsync(eventId, "invoice.payment_failed", row);
        await WebhookAsync(eventId, "invoice.payment_failed", row);

        (await NotificationTypesAsync(user)).Count(t => t == NotificationType.PaymentFailed).ShouldBe(1);
        (await Query().SubscriptionEvents.CountAsync(e => e.ProviderEventID == eventId)).ShouldBe(1);
    }

    [Fact]
    public async Task Webhook_OldEventArrivingLate_DoesntUndoNewerState()
    {
        var user = await SubscribedUserAsync();
        var row = await RowAsync(user);
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.PaymentFailed);
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.Renewed);

        // The "renewed" news first, then the older "payment failed" one
        await WebhookAsync(TestData.Unique("evt_"), "invoice.paid", row);
        await WebhookAsync(TestData.Unique("evt_"), "invoice.payment_failed", row);

        var status = await Subscriptions.GetStatusAsync(user.ID);
        status.Status.ShouldBe("Active");
        status.HasPaymentProblem.ShouldBeFalse();
        (await NotificationTypesAsync(user)).ShouldNotContain(NotificationType.PaymentFailed);
    }

    [Fact]
    public async Task Webhook_ProviderEndedTheSubscription_DowngradesAndNotifies()
    {
        var user = await SubscribedUserAsync();
        var row = await RowAsync(user);
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.Ended);

        await WebhookAsync(TestData.Unique("evt_"), "customer.subscription.deleted", row);

        (await Subscriptions.GetStatusAsync(user.ID)).IsPremium.ShouldBeFalse();
        (await NotificationTypesAsync(user)).ShouldContain(NotificationType.SubscriptionEnded);
        await App.Mail.WaitForAsync(user.Email, "Your Premium subscription has ended");
    }

    [Fact]
    public async Task Webhook_UnknownCustomer_IsAcknowledgedAndIgnored()
    {
        var eventId = TestData.Unique("evt_");

        (await Subscriptions.HandleWebhookAsync(
            TestPaymentProvider.WebhookPayload(eventId, "invoice.paid", "cus_unknown", "sub_unknown"), TestPaymentProvider.ValidSignature)).ShouldBeTrue();
    }

    [Fact]
    public async Task Plan_ListsPremiumFeaturesWithTheConfiguredPrice()
    {
        var plan = Subscriptions.GetPlan();

        plan.Tier.ShouldBe("Premium");
        plan.PriceAmount.ShouldBe(4.99m);
        plan.Currency.ShouldBe("EUR");
        plan.Features.ShouldContain(f => f.ComingSoon);
        plan.Features.ShouldContain(f => !f.ComingSoon);
    }
}
