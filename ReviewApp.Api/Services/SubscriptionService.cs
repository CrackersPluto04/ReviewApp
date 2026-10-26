using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Services;

// The local subscription record only changes from what the provider reports (a fetched snapshot), never from
// what a client claims. Provider notifications are just triggers to fetch the current state, which makes them
// idempotent and order-independent; the user is notified about local state changes only.
public class SubscriptionService : ISubscriptionService
{
    private const string ProviderUnavailable = "The payment service is unavailable right now. Please try again later.";
    private const int MaxApplyAttempts = 3;

    // Everything else is acknowledged and ignored
    private static readonly HashSet<string> RelevantEvents =
    [
        "checkout.session.completed",
        "customer.subscription.created",
        "customer.subscription.updated",
        "customer.subscription.deleted",
        "invoice.paid",
        "invoice.payment_failed"
    ];

    private readonly AppDbContext _context;
    private readonly IPaymentProvider _provider;
    private readonly ISubscriptionNotifier _notifier;
    private readonly SubscriptionOptions _options;
    private readonly string _frontendBaseUrl;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(AppDbContext context, IPaymentProvider provider, ISubscriptionNotifier notifier,
        IOptions<SubscriptionOptions> options, IConfiguration configuration, ILogger<SubscriptionService> logger)
    {
        _context = context;
        _provider = provider;
        _notifier = notifier;
        _options = options.Value;
        _frontendBaseUrl = configuration["App:FrontendBaseUrl"]!.TrimEnd('/');
        _logger = logger;
    }

    public PlanDto GetPlan() => new()
    {
        Tier = SubscriptionTier.Premium.ToString(),
        PriceAmount = _options.PriceAmount,
        Currency = _options.Currency,
        Interval = _options.Interval,
        Features = Entitlements.Catalog
            .Where(f => f.MinimumTier == SubscriptionTier.Premium)
            .Select(f => new PlanFeatureDto { Key = f.Key, Title = f.Title, Description = f.Description, ComingSoon = f.ComingSoon })
            .ToList()
    };

    public async Task<SubscriptionStatusDto> GetStatusAsync(int userId)
    {
        var subscription = await _context.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserID == userId);
        return ToDto(subscription.ToState(), DateTime.UtcNow);
    }

    public async Task<(bool Success, string Message, string? Url)> StartCheckoutAsync(int userId, bool android)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return (false, "User not found.", null);

        var subscription = await _context.UserSubscriptions.FirstOrDefaultAsync(s => s.UserID == userId);
        var state = subscription.ToState();
        var now = DateTime.UtcNow;

        if (state.Status == SubscriptionStatus.PastDue)
            return (false, "Your last payment failed. Update your payment details instead of subscribing again.", null);
        if (SubscriptionRules.IsEntitled(state, now))
            return (false, state.CancelAtPeriodEnd
                ? "Your Premium subscription is still running. Resume it instead of subscribing again."
                : "You already have Premium.", null);

        try
        {
            // The provider customer is created here, so every later notification maps back to this user
            if (subscription == null || subscription.Provider != _provider.Kind)
            {
                var customerId = await _provider.CreateCustomerAsync(userId, user.Email, user.Username);
                if (subscription == null)
                {
                    subscription = new UserSubscription { UserID = userId, CreatedAt = now };
                    _context.UserSubscriptions.Add(subscription);
                }

                // A record from another provider (e.g. Fake in development before switching to Stripe) starts over
                subscription.Provider = _provider.Kind;
                subscription.ProviderCustomerID = customerId;
                subscription.ProviderSubscriptionID = null;
                subscription.CopyFrom(SubscriptionState.Empty);
                subscription.UpdatedAt = now;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // A parallel request (double click, second tab) created it first
                    return (false, "A checkout is already being prepared. Please try again.", null);
                }
            }

            var url = await _provider.CreateCheckoutUrlAsync(userId, subscription.ProviderCustomerID,
                ReturnUrl(android, "success"), ReturnUrl(android, "cancelled"));
            return (true, "Redirecting to checkout.", url);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogError(ex, "Starting checkout failed for user {UserId}", userId);
            return (false, ProviderUnavailable, null);
        }
    }

    public async Task<(bool Success, string Message, string? Url)> CreatePortalAsync(int userId, bool android)
    {
        var subscription = await _context.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserID == userId);
        if (subscription?.ProviderSubscriptionID == null || subscription.Provider != _provider.Kind)
            return (false, "You don't have a subscription to manage.", null);

        try
        {
            var url = await _provider.CreatePortalUrlAsync(subscription.ProviderCustomerID,
                android ? ReturnUrl(true, "portal") : $"{_frontendBaseUrl}/premium");
            return (true, "Redirecting to the payment settings.", url);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogError(ex, "Opening the billing portal failed for user {UserId}", userId);
            return (false, ProviderUnavailable, null);
        }
    }

    public async Task<(bool Success, string Message)> CancelAsync(int userId)
    {
        var subscription = await FindRunningAsync(userId);
        if (subscription == null) return (false, "You don't have an active subscription.");
        if (subscription.CancelAtPeriodEnd) return (true, "Your subscription is already cancelled.");

        try
        {
            var next = await ApplySnapshotAsync(await _provider.SetCancelAtPeriodEndAsync(subscription.ProviderSubscriptionID!, true));
            var until = SubscriptionRules.AccessEndsAt(next ?? subscription.ToState());
            return (true, until is { } date
                ? $"Your subscription is cancelled. You keep Premium until {SubscriptionText.Date(date)}."
                : "Your subscription is cancelled.");
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogError(ex, "Cancelling the subscription failed for user {UserId}", userId);
            return (false, ProviderUnavailable);
        }
    }

    public async Task<(bool Success, string Message)> ResumeAsync(int userId)
    {
        var subscription = await FindRunningAsync(userId);
        if (subscription == null) return (false, "Your subscription has already ended. You can subscribe again instead.");
        if (!subscription.CancelAtPeriodEnd) return (true, "Your subscription is already active.");

        try
        {
            await ApplySnapshotAsync(await _provider.SetCancelAtPeriodEndAsync(subscription.ProviderSubscriptionID!, false));
            return (true, "Your subscription will renew again.");
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogError(ex, "Resuming the subscription failed for user {UserId}", userId);
            return (false, ProviderUnavailable);
        }
    }

    public async Task<SubscriptionStatusDto> SyncAsync(int userId)
    {
        var subscription = await _context.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserID == userId);
        if (subscription != null && subscription.Provider == _provider.Kind)
        {
            try
            {
                if (await _provider.FindLatestSubscriptionAsync(subscription.ProviderCustomerID) is { } snapshot)
                    await ApplySnapshotAsync(snapshot);
            }
            catch (PaymentProviderException ex)
            {
                // The stored state is still the best answer
                _logger.LogWarning(ex, "Subscription sync failed for user {UserId}", userId);
            }
        }

        return await GetStatusAsync(userId);
    }

    public async Task<bool> HandleWebhookAsync(string payload, string? signature)
    {
        var webhookEvent = _provider.ParseWebhook(payload, signature);
        if (webhookEvent == null) return false;
        if (!RelevantEvents.Contains(webhookEvent.Type)) return true;

        var record = await _context.SubscriptionEvents.FirstOrDefaultAsync(e => e.ProviderEventID == webhookEvent.EventID);
        if (record?.ProcessedAt != null) return true; // a repeated delivery

        if (record == null)
        {
            record = new SubscriptionEvent { ProviderEventID = webhookEvent.EventID, Type = webhookEvent.Type };
            _context.SubscriptionEvents.Add(record);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A parallel delivery of the same event is being processed
                return true;
            }
        }

        try
        {
            ProviderSubscriptionSnapshot? snapshot = null;
            if (webhookEvent.SubscriptionID != null)
                snapshot = await _provider.GetSubscriptionAsync(webhookEvent.SubscriptionID);
            else if (webhookEvent.CustomerID != null)
                snapshot = await _provider.FindLatestSubscriptionAsync(webhookEvent.CustomerID);

            if (snapshot != null)
                await ApplySnapshotAsync(snapshot);

            await _context.SubscriptionEvents
                .Where(e => e.ID == record.ID)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ProcessedAt, DateTime.UtcNow));

            _logger.LogInformation("Processed subscription event {EventId} ({EventType})", webhookEvent.EventID, webhookEvent.Type);
            return true;
        }
        catch (Exception ex)
        {
            // Left unprocessed: the provider retries the delivery and it is picked up again
            _logger.LogError(ex, "Processing subscription event {EventId} ({EventType}) failed", webhookEvent.EventID, webhookEvent.Type);
            throw;
        }
    }

    public async Task<int> ExpireLapsedAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - SubscriptionRules.RenewalSlack;
        var userIds = await _context.UserSubscriptions.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd < cutoff))
                     || s.Status == SubscriptionStatus.PastDue)
            .Select(s => s.UserID)
            .ToListAsync(cancellationToken);

        var ended = 0;
        foreach (var userId in userIds)
        {
            _context.ChangeTracker.Clear();
            var subscription = await _context.UserSubscriptions.FirstOrDefaultAsync(s => s.UserID == userId, cancellationToken);
            if (subscription == null) continue;

            // Ask the provider first: the renewal may have happened and only its notification was lost
            if (subscription.ProviderSubscriptionID != null && subscription.Provider == _provider.Kind)
            {
                try
                {
                    if (await _provider.GetSubscriptionAsync(subscription.ProviderSubscriptionID) is { } snapshot)
                        await ApplySnapshotAsync(snapshot);
                }
                catch (PaymentProviderException ex)
                {
                    _logger.LogWarning(ex, "Expiry check could not reach the payment provider for user {UserId}", userId);
                }

                _context.ChangeTracker.Clear();
                subscription = await _context.UserSubscriptions.FirstOrDefaultAsync(s => s.UserID == userId, cancellationToken);
                if (subscription == null) continue;
            }

            var previous = subscription.ToState();
            var next = SubscriptionRules.ExpireIfLapsed(previous, DateTime.UtcNow);
            if (next == null) continue;

            subscription.CopyFrom(next);
            subscription.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue; // changed meanwhile, the next run looks again
            }

            await _notifier.NotifyAsync(userId, SubscriptionRules.DetectTransitions(previous, next), next);
            ended++;
        }

        return ended;
    }

    public async Task<(bool Success, string Message)> SimulateAsync(int userId, FakeSubscriptionEvent simulated)
    {
        if (_provider is not FakePaymentProvider fake)
            return (false, "Simulations only work with the Fake provider.");

        var subscription = await _context.UserSubscriptions.FirstOrDefaultAsync(s => s.UserID == userId);
        if (subscription?.ProviderSubscriptionID == null || subscription.Provider != SubscriptionProvider.Fake)
            return (false, "Start a (fake) subscription first.");

        try
        {
            var snapshot = await fake.SimulateAsync(subscription.ProviderSubscriptionID, simulated);

            if (simulated == FakeSubscriptionEvent.Lapsed)
            {
                // The provider "forgot" to tell us: only the period end changes, the expiry job ends it later
                subscription.CurrentPeriodEnd = snapshot.CurrentPeriodEnd;
                subscription.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return (true, "The period ran out without a notification. Premium is gone now; the expiry job marks the subscription ended within 15 minutes.");
            }

            await ApplySnapshotAsync(snapshot);
            return (true, $"Simulated {simulated}.");
        }
        catch (PaymentProviderException ex)
        {
            return (false, ex.Message);
        }
    }

    // Applies the provider's view to the local record and notifies about the changes. Returns the new state,
    // or null when the snapshot was ignored.
    private async Task<SubscriptionState?> ApplySnapshotAsync(ProviderSubscriptionSnapshot snapshot)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryApplySnapshotAsync(snapshot);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxApplyAttempts)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }

    private async Task<SubscriptionState?> TryApplySnapshotAsync(ProviderSubscriptionSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var subscription = await _context.UserSubscriptions.FirstOrDefaultAsync(s => s.ProviderCustomerID == snapshot.CustomerID);
        if (subscription == null)
        {
            // Customers are only created by StartCheckoutAsync, so this isn't ours
            _logger.LogWarning("Ignored subscription {SubscriptionId}: unknown customer", snapshot.SubscriptionID);
            return null;
        }

        var previous = subscription.ToState();
        var incomingRunning = snapshot.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

        if (subscription.ProviderSubscriptionID != null && subscription.ProviderSubscriptionID != snapshot.SubscriptionID)
        {
            var currentRunning = SubscriptionRules.IsEntitled(previous, now);
            if (currentRunning && incomingRunning)
            {
                // Two checkouts completed (e.g. two tabs): keep the first, stop the second before it renews
                _logger.LogWarning("User {UserId} has a second subscription {SubscriptionId}, cancelling it", subscription.UserID, snapshot.SubscriptionID);
                await _provider.CancelImmediatelyAsync(snapshot.SubscriptionID);
                return null;
            }

            // News about an older or abandoned subscription doesn't replace the current one
            if (currentRunning || !incomingRunning)
                return null;
        }

        var next = SubscriptionRules.Apply(previous, snapshot, now);
        if (next == previous && subscription.ProviderSubscriptionID == snapshot.SubscriptionID)
            return next;

        subscription.ProviderSubscriptionID = snapshot.SubscriptionID;
        subscription.CopyFrom(next);
        subscription.UpdatedAt = now;
        await _context.SaveChangesAsync();

        var transitions = SubscriptionRules.DetectTransitions(previous, next);
        if (transitions != SubscriptionTransitions.None)
            await _notifier.NotifyAsync(subscription.UserID, transitions, next);

        return next;
    }

    private async Task<UserSubscription?> FindRunningAsync(int userId)
    {
        var subscription = await _context.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserID == userId);
        var running = subscription?.ProviderSubscriptionID != null
            && subscription.Provider == _provider.Kind
            && SubscriptionRules.IsEntitled(subscription.ToState(), DateTime.UtcNow);
        return running ? subscription : null;
    }

    private string ReturnUrl(bool android, string result) =>
        BuildReturnUrl(_frontendBaseUrl, _options.MobileReturnBaseUrl, android, result);

    // Where the provider's hosted pages send the user back to: the web return page, or the API page that opens the Android app
    public static string BuildReturnUrl(string frontendBaseUrl, string mobileReturnBaseUrl, bool android, string result) =>
        android
            ? $"{mobileReturnBaseUrl.TrimEnd('/')}/api/Subscription/return/android?result={Uri.EscapeDataString(result)}"
            : $"{frontendBaseUrl.TrimEnd('/')}/subscription/return?status={Uri.EscapeDataString(result)}";

    public static SubscriptionStatusDto ToDto(SubscriptionState state, DateTime now)
    {
        var entitled = SubscriptionRules.IsEntitled(state, now);
        var running = state.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;
        // Until the expiry job catches up, a lapsed subscription is shown as what it effectively is
        var status = running && !entitled ? SubscriptionStatus.Ended : state.Status;

        return new SubscriptionStatusDto
        {
            Tier = (entitled ? SubscriptionTier.Premium : SubscriptionTier.Free).ToString(),
            Status = status.ToString(),
            IsPremium = entitled,
            WillRenew = entitled && SubscriptionRules.WillRenew(state),
            CancelAtPeriodEnd = entitled && state.CancelAtPeriodEnd,
            HasPaymentProblem = entitled && state.Status == SubscriptionStatus.PastDue,
            CurrentPeriodEnd = state.CurrentPeriodEnd,
            AccessEndsAt = entitled ? SubscriptionRules.AccessEndsAt(state) : null
        };
    }
}
