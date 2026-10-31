using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The work of the daily cleanup and the subscription expiry job, run directly (the loops around them are just timers)
[Collection(GlobalJobsCollection.Name)]
public class BackgroundJobTests : IntegrationTest
{
    private Task RunCleanupAsync() =>
        new DataCleanupService(App.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataCleanupService>.Instance)
            .CleanupAsync(CancellationToken.None);

    [Fact]
    public async Task Cleanup_DeletesOnlyOldSeenNotifications()
    {
        var user = await Data.UserAsync();
        var now = DateTime.UtcNow;
        var oldSeen = await Data.NotificationAsync(user, seen: true, createdAt: now.AddDays(-91));
        var oldUnseen = await Data.NotificationAsync(user, seen: false, createdAt: now.AddDays(-91));
        var recentSeen = await Data.NotificationAsync(user, seen: true, createdAt: now.AddDays(-89));

        await RunCleanupAsync();

        var left = await Query().Notifications.Where(n => n.RecipientUserID == user.ID).Select(n => n.ID).ToListAsync();
        left.ShouldBe([oldUnseen.ID, recentSeen.ID], ignoreOrder: true);
        left.ShouldNotContain(oldSeen.ID);
    }

    [Fact]
    public async Task Cleanup_DeletesExpiredAndSpentTokensAfterADay()
    {
        var user = await Data.UserAsync();
        var now = DateTime.UtcNow;
        var (expiredLongAgo, _) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation, expiresAt: now.AddDays(-2), createdAt: now.AddDays(-3));
        var (usedLongAgo, _) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorLogin, used: true, createdAt: now.AddDays(-2), expiresAt: now.AddDays(1));
        var (justExpired, _) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailChange, expiresAt: now.AddHours(-1), createdAt: now.AddHours(-2));
        var (justUsed, _) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorEnable, used: true);
        var (valid, _) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorDisable);

        await RunCleanupAsync();

        var left = await Query().SecurityTokens.Where(t => t.UserID == user.ID).Select(t => t.ID).ToListAsync();
        left.ShouldBe([justExpired.ID, justUsed.ID, valid.ID], ignoreOrder: true);
        left.ShouldNotContain(expiredLongAgo.ID);
        left.ShouldNotContain(usedLongAgo.ID);
    }

    private Task<int> RunExpiryAsync() => Service<ISubscriptionService>().ExpireLapsedAsync(CancellationToken.None);

    [Fact]
    public async Task Expiry_EndsALapsedSubscriptionTheProviderNeverReported()
    {
        var user = await Data.UserAsync();
        var lapsed = await Data.SubscriptionAsync(user, SubscriptionStatus.Active, DateTime.UtcNow - SubscriptionRules.RenewalSlack - TimeSpan.FromMinutes(5));

        (await RunExpiryAsync()).ShouldBeGreaterThanOrEqualTo(1);

        (await Query().UserSubscriptions.SingleAsync(s => s.ID == lapsed.ID)).Status.ShouldBe(SubscriptionStatus.Ended);
        (await Query().Notifications.AnyAsync(n => n.RecipientUserID == user.ID && n.Type == NotificationType.SubscriptionEnded)).ShouldBeTrue();
        await App.Mail.WaitForAsync(user.Email, "Your Premium subscription has ended");
    }

    [Fact]
    public async Task Expiry_EndsAPastDueSubscriptionAfterTheGracePeriod()
    {
        var user = await Data.UserAsync();
        var now = DateTime.UtcNow;
        var overdue = await Data.SubscriptionAsync(user, SubscriptionStatus.PastDue, now.AddDays(-6), pastDueSince: now.AddDays(-6));

        await RunExpiryAsync();

        (await Query().UserSubscriptions.SingleAsync(s => s.ID == overdue.ID)).Status.ShouldBe(SubscriptionStatus.Ended);
    }

    [Fact]
    public async Task Expiry_LeavesRunningSubscriptionsAlone()
    {
        var now = DateTime.UtcNow;
        var active = await Data.SubscriptionAsync(await Data.UserAsync(), SubscriptionStatus.Active, now.AddDays(3));
        var withinSlack = await Data.SubscriptionAsync(await Data.UserAsync(), SubscriptionStatus.Active, now.AddMinutes(-30));
        var inGrace = await Data.SubscriptionAsync(await Data.UserAsync(), SubscriptionStatus.PastDue, now.AddDays(-1), pastDueSince: now.AddDays(-1));

        await RunExpiryAsync();

        var statuses = await Query().UserSubscriptions.Where(s => s.ID == active.ID || s.ID == withinSlack.ID || s.ID == inGrace.ID)
            .Select(s => s.Status).ToListAsync();
        statuses.ShouldBe([SubscriptionStatus.Active, SubscriptionStatus.Active, SubscriptionStatus.PastDue], ignoreOrder: true);
    }

    [Fact]
    public async Task Expiry_AsksTheProviderFirstAndKeepsARenewalWhoseNoticeWasLost()
    {
        var user = await Data.UserAsync();
        var subscriptions = Service<ISubscriptionService>();
        await subscriptions.StartCheckoutAsync(user.ID, android: false);
        await subscriptions.SyncAsync(user.ID);
        var row = await Query().UserSubscriptions.SingleAsync(s => s.UserID == user.ID);
        // The provider renewed, but the local row still has the old, lapsed period
        await App.Payments.SimulateAsync(row.ProviderSubscriptionID!, FakeSubscriptionEvent.Renewed);
        await Query().UserSubscriptions.Where(s => s.ID == row.ID)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEnd, DateTime.UtcNow.AddDays(-1)));

        await RunExpiryAsync();

        var stored = await Query().UserSubscriptions.SingleAsync(s => s.ID == row.ID);
        stored.Status.ShouldBe(SubscriptionStatus.Active);
        stored.CurrentPeriodEnd!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(29));
    }
}
