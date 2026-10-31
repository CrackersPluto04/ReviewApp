using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Payments;

// Events the Fake provider can simulate (POST api/Subscription/dev/simulate, Development only)
public enum FakeSubscriptionEvent
{
    PaymentFailed, // a renewal payment failed
    Renewed,       // a renewal (or a retried payment) went through
    Ended,         // the provider ended the subscription
    Lapsed         // the period ran out and the provider never said so (tests the expiry safety net)
}

// Development-only provider: "checkout" activates a 30 day subscription at once, with no payment and no external calls.
// Program.cs refuses to start with it outside Development. State lives in memory; after a restart it is rebuilt
// from the stored rows, so a running fake subscription keeps working.
public class FakePaymentProvider : IPaymentProvider
{
    public static readonly TimeSpan PeriodLength = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, ProviderSubscriptionSnapshot> _subscriptions = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public FakePaymentProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public SubscriptionProvider Kind => SubscriptionProvider.Fake;

    public Task<string> CreateCustomerAsync(int userId, string email, string username) =>
        Task.FromResult($"fake_cus_{userId}_{Guid.NewGuid():N}"[..40]);

    public Task<string> CreateCheckoutUrlAsync(int userId, string customerId, string successUrl, string cancelUrl)
    {
        var subscriptionId = $"fake_sub_{Guid.NewGuid():N}";
        _subscriptions[subscriptionId] = new ProviderSubscriptionSnapshot(
            customerId, subscriptionId, SubscriptionStatus.Active, false, DateTime.UtcNow + PeriodLength);

        // Straight back to the app, which syncs and finds the new subscription
        return Task.FromResult(successUrl);
    }

    public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl) => Task.FromResult(returnUrl);

    public async Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId) =>
        _subscriptions.TryGetValue(subscriptionId, out var snapshot)
            ? snapshot
            : await LoadAsync(s => s.ProviderSubscriptionID == subscriptionId);

    public async Task<ProviderSubscriptionSnapshot?> FindLatestSubscriptionAsync(string customerId)
    {
        var own = _subscriptions.Values.Where(s => s.CustomerID == customerId).ToList();
        var running = own.FirstOrDefault(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue);
        return running ?? own.FirstOrDefault() ?? await LoadAsync(s => s.ProviderCustomerID == customerId);
    }

    public async Task<ProviderSubscriptionSnapshot> SetCancelAtPeriodEndAsync(string subscriptionId, bool cancel)
    {
        var current = await GetSubscriptionAsync(subscriptionId)
            ?? throw new PaymentProviderException("Unknown fake subscription.");
        return _subscriptions[subscriptionId] = current with { CancelAtPeriodEnd = cancel };
    }

    public async Task CancelImmediatelyAsync(string subscriptionId)
    {
        if (await GetSubscriptionAsync(subscriptionId) is { } current)
            _subscriptions[subscriptionId] = current with { Status = SubscriptionStatus.Ended, CancelAtPeriodEnd = false };
    }

    // The Fake provider has no webhooks: changes are applied right away by the service
    public PaymentWebhookEvent? ParseWebhook(string payload, string? signature) => null;

    public async Task<ProviderSubscriptionSnapshot> SimulateAsync(string subscriptionId, FakeSubscriptionEvent simulated)
    {
        var current = await GetSubscriptionAsync(subscriptionId)
            ?? throw new PaymentProviderException("Unknown fake subscription.");
        var now = DateTime.UtcNow;

        var next = simulated switch
        {
            FakeSubscriptionEvent.PaymentFailed => current with { Status = SubscriptionStatus.PastDue },
            // A cancelled subscription doesn't renew, it ends
            FakeSubscriptionEvent.Renewed when current.CancelAtPeriodEnd => current with { Status = SubscriptionStatus.Ended, CancelAtPeriodEnd = false },
            FakeSubscriptionEvent.Renewed => current with
            {
                Status = SubscriptionStatus.Active,
                CurrentPeriodEnd = (current.CurrentPeriodEnd > now ? current.CurrentPeriodEnd.Value : now) + PeriodLength
            },
            FakeSubscriptionEvent.Ended => current with { Status = SubscriptionStatus.Ended, CancelAtPeriodEnd = false },
            // Past the period end and the renewal slack, so the user is no longer entitled
            FakeSubscriptionEvent.Lapsed => current with { CurrentPeriodEnd = now - SubscriptionRules.RenewalSlack - TimeSpan.FromMinutes(1) },
            _ => current
        };

        return _subscriptions[subscriptionId] = next;
    }

    private async Task<ProviderSubscriptionSnapshot?> LoadAsync(Expression<Func<UserSubscription, bool>> predicate)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await context.UserSubscriptions.AsNoTracking()
            .Where(s => s.Provider == SubscriptionProvider.Fake && s.ProviderSubscriptionID != null)
            .Where(predicate)
            .FirstOrDefaultAsync();
        if (row == null) return null;

        var state = row.ToState();
        var snapshot = new ProviderSubscriptionSnapshot(row.ProviderCustomerID, row.ProviderSubscriptionID!,
            state.Status, state.CancelAtPeriodEnd, state.CurrentPeriodEnd);
        _subscriptions.TryAdd(snapshot.SubscriptionID, snapshot);
        return snapshot;
    }
}
