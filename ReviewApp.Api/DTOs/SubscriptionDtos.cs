using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// The current user's subscription. Never contains provider IDs.
/// </summary>
public record SubscriptionStatusDto
{
    /// <summary><c>Free</c> or <c>Premium</c>: what the user can use right now.</summary>
    public string Tier { get; init; } = "Free";
    /// <summary><c>None</c>, <c>Active</c>, <c>PastDue</c> or <c>Ended</c>: the stored subscription state, which can differ from <see cref="Tier"/> (e.g. grace periods).</summary>
    public string Status { get; init; } = "None";
    public bool IsPremium { get; init; }
    public bool WillRenew { get; init; }
    /// <summary>Cancelled, but Premium keeps running until <see cref="AccessEndsAt"/>.</summary>
    public bool CancelAtPeriodEnd { get; init; }
    /// <summary>A renewal failed, but Premium keeps running until <see cref="AccessEndsAt"/> (the grace period).</summary>
    public bool HasPaymentProblem { get; init; }
    /// <summary>End of the paid billing period, UTC.</summary>
    public DateTime? CurrentPeriodEnd { get; init; }
    /// <summary>When Premium access stops, UTC: the period end, or the end of the payment grace period after a failed payment. Null when not entitled.</summary>
    public DateTime? AccessEndsAt { get; init; }
}

/// <summary>
/// One feature on the plans page.
/// </summary>
public record PlanFeatureDto
{
    /// <summary>Stable feature key, the same one <c>[RequiresFeature]</c> endpoints use.</summary>
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>Listed on the plans page but not granted yet.</summary>
    public bool ComingSoon { get; init; }
}

/// <summary>
/// The Premium plan as shown before and after login. The price shown must match the payment provider's price.
/// </summary>
public record PlanDto
{
    public string Tier { get; init; } = "Premium";
    public decimal PriceAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public IReadOnlyList<PlanFeatureDto> Features { get; init; } = [];
}

/// <summary>
/// Which client is asking; decides where the provider's pages send the user back to.
/// </summary>
public record SubscriptionRedirectDto
{
    /// <summary><c>web</c> or <c>android</c>.</summary>
    [RegularExpression("^(web|android)$", ErrorMessage = "Platform must be 'web' or 'android'.")]
    public string Platform { get; init; } = "web";
}

/// <summary>
/// A provider page (checkout or customer portal) the client should send the user to.
/// </summary>
public record SubscriptionUrlDto
{
    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// Development-only request to play a provider event against the Fake provider.
/// </summary>
public record SimulateSubscriptionEventDto
{
    /// <summary><c>Lapsed</c> means the period ended without a notification, which exercises the expiry job.</summary>
    [Required, RegularExpression("^(PaymentFailed|Renewed|Ended|Lapsed)$",
        ErrorMessage = "Event must be PaymentFailed, Renewed, Ended or Lapsed.")]
    public string Event { get; init; } = string.Empty;
}
