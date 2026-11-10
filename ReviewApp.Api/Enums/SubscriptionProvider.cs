namespace ReviewApp.Api.Enums;

/// <summary>
/// The payment provider behind a subscription, chosen with <c>Subscription:Provider</c> and stored on each
/// subscription row, so a record made with another provider starts over when the provider changes.
/// </summary>
public enum SubscriptionProvider
{
    Stripe,
    /// <summary>Development only, no real payment.</summary>
    Fake
}
