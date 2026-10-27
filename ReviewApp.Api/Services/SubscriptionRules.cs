using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// The stored subscription state the rules work on (a copy of the UserSubscription columns that matter).
public record SubscriptionState(
    SubscriptionStatus Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd,
    DateTime? PastDueSince)
{
    public static readonly SubscriptionState Empty = new(SubscriptionStatus.None, false, null, null);
}

// What the payment provider currently reports about a subscription.
public record ProviderSubscriptionSnapshot(
    string CustomerID,
    string SubscriptionID,
    SubscriptionStatus Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd);

// Local state changes that the user is told about (in-app notification + email).
[Flags]
public enum SubscriptionTransitions
{
    None = 0,
    Started = 1,
    PaymentFailed = 2,
    Cancelled = 4,
    Ended = 8
}

// Pure subscription logic (no database, no provider): who is Premium, how a provider snapshot
// changes the stored state, and which changes are worth telling the user about.
// All times are UTC.
public static class SubscriptionRules
{
    // A renewal can land a little after the period end; keep access meanwhile so it doesn't flap.
    public static readonly TimeSpan RenewalSlack = TimeSpan.FromHours(2);

    // Access kept after a failed renewal payment while the provider retries.
    public static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(5);

    public static bool IsEntitled(SubscriptionState state, DateTime now)
    {
        switch (state.Status)
        {
            case SubscriptionStatus.Active:
                // A missed "ended" notification can't grant Premium forever: the stored period end still limits it
                return state.CurrentPeriodEnd is { } end && now <= end + RenewalSlack;

            case SubscriptionStatus.PastDue:
                var since = state.PastDueSince ?? state.CurrentPeriodEnd;
                return since is { } start && now <= start + PastDueGrace;

            default:
                return false;
        }
    }

    public static SubscriptionTier GetTier(SubscriptionState state, DateTime now) =>
        IsEntitled(state, now) ? SubscriptionTier.Premium : SubscriptionTier.Free;

    public static bool WillRenew(SubscriptionState state) =>
        state.Status == SubscriptionStatus.Active && !state.CancelAtPeriodEnd;

    // When Premium stops unless something changes: the period end, or the end of the grace period after a failed payment.
    // Shown to the user, so it leaves out the renewal slack.
    public static DateTime? AccessEndsAt(SubscriptionState state) => state.Status switch
    {
        SubscriptionStatus.Active => state.CurrentPeriodEnd,
        SubscriptionStatus.PastDue => (state.PastDueSince ?? state.CurrentPeriodEnd) + PastDueGrace,
        _ => null
    };

    // Builds the new stored state from what the provider reports.
    public static SubscriptionState Apply(SubscriptionState current, ProviderSubscriptionSnapshot snapshot, DateTime now)
    {
        // The grace period counts from the first failed payment, not from the latest retry
        DateTime? pastDueSince = null;
        if (snapshot.Status == SubscriptionStatus.PastDue)
            pastDueSince = current.Status == SubscriptionStatus.PastDue ? current.PastDueSince ?? now : now;

        return new SubscriptionState(
            snapshot.Status,
            // An ended subscription has nothing left to cancel
            snapshot.Status != SubscriptionStatus.Ended && snapshot.CancelAtPeriodEnd,
            snapshot.CurrentPeriodEnd ?? current.CurrentPeriodEnd,
            pastDueSince);
    }

    // Used by the expiry job when the provider's "ended" notification never arrived.
    public static SubscriptionState? ExpireIfLapsed(SubscriptionState state, DateTime now)
    {
        var running = state.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;
        if (!running || IsEntitled(state, now))
            return null;

        return state with { Status = SubscriptionStatus.Ended, CancelAtPeriodEnd = false, PastDueSince = null };
    }

    public static SubscriptionTransitions DetectTransitions(SubscriptionState previous, SubscriptionState next)
    {
        var result = SubscriptionTransitions.None;
        var wasRunning = previous.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;
        var isRunning = next.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

        // Recovering from PastDue is not a new start
        if (next.Status == SubscriptionStatus.Active && !wasRunning)
            result |= SubscriptionTransitions.Started;

        if (next.Status == SubscriptionStatus.PastDue && previous.Status != SubscriptionStatus.PastDue)
            result |= SubscriptionTransitions.PaymentFailed;

        if (wasRunning && isRunning && next.CancelAtPeriodEnd && !previous.CancelAtPeriodEnd)
            result |= SubscriptionTransitions.Cancelled;

        if (wasRunning && next.Status == SubscriptionStatus.Ended)
            result |= SubscriptionTransitions.Ended;

        return result;
    }

    // Stripe subscription statuses (https://docs.stripe.com/billing/subscriptions/overview#subscription-statuses).
    public static SubscriptionStatus MapStripeStatus(string? status) => status switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" or "paused" => SubscriptionStatus.Ended,
        _ => SubscriptionStatus.None // "incomplete": the first payment hasn't gone through yet
    };

    // Stripe reports a cancellation in two ways: cancel_at_period_end (our cancel endpoint) or a cancel_at date
    // (the customer portal on newer API versions). A cancel_at after the current period means it renews once more.
    public static bool StripeEndsAtPeriodEnd(bool cancelAtPeriodEnd, DateTime? cancelAt, DateTime? periodEnd) =>
        cancelAtPeriodEnd || (cancelAt is { } at && periodEnd is { } end && at <= end);

    public static SubscriptionState ToState(this UserSubscription? subscription) =>
        subscription == null
            ? SubscriptionState.Empty
            : new SubscriptionState(
                subscription.Status,
                subscription.CancelAtPeriodEnd,
                AsUtc(subscription.CurrentPeriodEnd),
                AsUtc(subscription.PastDueSince));

    public static void CopyFrom(this UserSubscription subscription, SubscriptionState state)
    {
        subscription.Status = state.Status;
        subscription.CancelAtPeriodEnd = state.CancelAtPeriodEnd;
        subscription.CurrentPeriodEnd = state.CurrentPeriodEnd;
        subscription.PastDueSince = state.PastDueSince;
    }

    // Values read back from SQL Server are Unspecified
    private static DateTime? AsUtc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
