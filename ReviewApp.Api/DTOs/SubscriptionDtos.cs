using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

// The current user's subscription. Never contains provider IDs.
public record SubscriptionStatusDto
{
    public string Tier { get; init; } = "Free";      // Free / Premium: what the user can use right now
    public string Status { get; init; } = "None";    // None / Active / PastDue / Ended
    public bool IsPremium { get; init; }
    public bool WillRenew { get; init; }
    public bool CancelAtPeriodEnd { get; init; }     // cancelled, Premium runs until AccessEndsAt
    public bool HasPaymentProblem { get; init; }     // a renewal failed, Premium runs until AccessEndsAt (grace period)
    public DateTime? CurrentPeriodEnd { get; init; }
    public DateTime? AccessEndsAt { get; init; }
}

public record PlanFeatureDto
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool ComingSoon { get; init; }
}

public record PlanDto
{
    public string Tier { get; init; } = "Premium";
    public decimal PriceAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public IReadOnlyList<PlanFeatureDto> Features { get; init; } = [];
}

// Which client asks: decides where the provider's pages send the user back to
public record SubscriptionRedirectDto
{
    [RegularExpression("^(web|android)$", ErrorMessage = "Platform must be 'web' or 'android'.")]
    public string Platform { get; init; } = "web";
}

public record SubscriptionUrlDto
{
    public string Url { get; init; } = string.Empty;
}

public record SimulateSubscriptionEventDto
{
    [Required, RegularExpression("^(PaymentFailed|Renewed|Ended|Lapsed)$",
        ErrorMessage = "Event must be PaymentFailed, Renewed, Ended or Lapsed.")]
    public string Event { get; init; } = string.Empty;
}
