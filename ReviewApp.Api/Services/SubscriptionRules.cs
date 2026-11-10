using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

/// <summary>
/// The stored subscription state the rules work on: a copy of the <c>UserSubscription</c> columns that matter,
/// so the rules need no database entity.
/// </summary>
/// <param name="Status">The stored status.</param>
/// <param name="CancelAtPeriodEnd">Cancelled, but running until the period end.</param>
/// <param name="CurrentPeriodEnd">End of the paid billing period, UTC.</param>
/// <param name="PastDueSince">When the first failed payment happened, UTC; the grace period counts from here.</param>
public record SubscriptionState(
    SubscriptionStatus Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd,
    DateTime? PastDueSince)
{
    /// <summary>The state of a user who never started a checkout.</summary>
    public static readonly SubscriptionState Empty = new(SubscriptionStatus.None, false, null, null);
}

/// <summary>
/// What the payment provider currently reports about a subscription. The only input that changes the stored state.
/// </summary>
public record ProviderSubscriptionSnapshot(
    string CustomerID,
    string SubscriptionID,
    SubscriptionStatus Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd);

/// <summary>
/// Local state changes that the user is told about (in-app notification and email). Flags, because one snapshot
/// can cause several at once.
/// </summary>
[Flags]
public enum SubscriptionTransitions
{
    None = 0,
    Started = 1,
    PaymentFailed = 2,
    Cancelled = 4,
    Ended = 8
}

/// <summary>
/// Pure subscription logic (no database, no provider): who is Premium, how a provider snapshot changes the stored
/// state, and which changes are worth telling the user about. Keeping it pure is what makes it fully unit-testable.
/// All times are UTC.
/// </summary>
public static class SubscriptionRules
{
    /// <summary>A renewal can land a little after the period end; access is kept meanwhile so it does not flap.</summary>
    public static readonly TimeSpan RenewalSlack = TimeSpan.FromHours(2);

    /// <summary>Access kept after a failed renewal payment while the provider retries.</summary>
    public static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(5);

    /// <summary>
    /// Whether the user may use Premium features at <paramref name="now"/>. Decided from the stored dates and not
    /// from the status alone, so a missed provider notification cannot grant Premium forever.
    /// </summary>
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

    /// <summary>The tier the user can use at <paramref name="now"/>.</summary>
    public static SubscriptionTier GetTier(SubscriptionState state, DateTime now) =>
        IsEntitled(state, now) ? SubscriptionTier.Premium : SubscriptionTier.Free;

    /// <summary>Whether the subscription will charge again at the period end.</summary>
    public static bool WillRenew(SubscriptionState state) =>
        state.Status == SubscriptionStatus.Active && !state.CancelAtPeriodEnd;

    /// <summary>
    /// When Premium stops unless something changes: the period end, or the end of the grace period after a failed
    /// payment. Shown to the user, so it leaves out the renewal slack.
    /// </summary>
    /// <returns>The date, or null when there is no running subscription.</returns>
    public static DateTime? AccessEndsAt(SubscriptionState state) => state.Status switch
    {
        SubscriptionStatus.Active => state.CurrentPeriodEnd,
        SubscriptionStatus.PastDue => (state.PastDueSince ?? state.CurrentPeriodEnd) + PastDueGrace,
        _ => null
    };

    /// <summary>Builds the new stored state from what the provider reports.</summary>
    /// <param name="current">The stored state.</param>
    /// <param name="snapshot">The provider's current view.</param>
    /// <param name="now">The current time, used to start the grace period.</param>
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

    /// <summary>Ends a running subscription whose access has run out. Used by the expiry job when the provider's "ended" notification never arrived.</summary>
    /// <returns>The ended state, or null when nothing needs to change.</returns>
    public static SubscriptionState? ExpireIfLapsed(SubscriptionState state, DateTime now)
    {
        var running = state.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;
        if (!running || IsEntitled(state, now))
            return null;

        return state with { Status = SubscriptionStatus.Ended, CancelAtPeriodEnd = false, PastDueSince = null };
    }

    /// <summary>
    /// Finds the changes between two stored states that are worth telling the user about. The notifications come
    /// from these local transitions and not from provider events, so a redelivered event cannot notify twice.
    /// </summary>
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

    /// <summary>
    /// Maps a Stripe subscription status to ours
    /// (<see href="https://docs.stripe.com/billing/subscriptions/overview#subscription-statuses"/>).
    /// </summary>
    public static SubscriptionStatus MapStripeStatus(string? status) => status switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" or "paused" => SubscriptionStatus.Ended,
        _ => SubscriptionStatus.None // "incomplete": the first payment hasn't gone through yet
    };

    /// <summary>
    /// Whether a Stripe subscription ends at the current period end. Stripe reports a cancellation in two ways:
    /// <c>cancel_at_period_end</c> (our cancel endpoint) or a <c>cancel_at</c> date (the customer portal on newer
    /// API versions). A <c>cancel_at</c> after the current period means it renews once more.
    /// </summary>
    public static bool StripeEndsAtPeriodEnd(bool cancelAtPeriodEnd, DateTime? cancelAt, DateTime? periodEnd) =>
        cancelAtPeriodEnd || (cancelAt is { } at && periodEnd is { } end && at <= end);

    /// <summary>Copies the rule-relevant columns of a stored row; a missing row (never subscribed) is <see cref="SubscriptionState.Empty"/>.</summary>
    public static SubscriptionState ToState(this UserSubscription? subscription) =>
        subscription == null
            ? SubscriptionState.Empty
            : new SubscriptionState(
                subscription.Status,
                subscription.CancelAtPeriodEnd,
                AsUtc(subscription.CurrentPeriodEnd),
                AsUtc(subscription.PastDueSince));

    /// <summary>Writes a state back to the stored row.</summary>
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
