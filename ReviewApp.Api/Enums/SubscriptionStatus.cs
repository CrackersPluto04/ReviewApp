namespace ReviewApp.Api.Enums;

/// <summary>
/// The stored state of a subscription, as last reported by the payment provider. Whether the user is actually
/// Premium is decided from this together with the stored dates (<c>SubscriptionRules</c>), not from the status alone.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>The row exists (a checkout was started) but nothing was ever paid.</summary>
    None,
    /// <summary>Paid. With <c>UserSubscription.CancelAtPeriodEnd</c> it is cancelled but runs until the period end.</summary>
    Active,
    /// <summary>A renewal payment failed; access continues for a grace period.</summary>
    PastDue,
    /// <summary>Over, the user is Free again.</summary>
    Ended
}
