using System.ComponentModel.DataAnnotations;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// The subscription record of a user: one row per user who ever started a checkout (no row means Free), reused when
/// they subscribe again. It only changes from what the payment provider reports. Never contains card data.
/// </summary>
public class UserSubscription
{
    public int ID { get; set; }
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    /// <summary>The plan this subscription is for.</summary>
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Premium;
    public SubscriptionProvider Provider { get; set; }

    [MaxLength(100)]
    public string ProviderCustomerID { get; set; } = string.Empty;

    /// <summary>Null until the first payment went through.</summary>
    [MaxLength(100)]
    public string? ProviderSubscriptionID { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.None;
    public bool CancelAtPeriodEnd { get; set; }
    /// <summary>End of the paid billing period, UTC.</summary>
    public DateTime? CurrentPeriodEnd { get; set; }
    /// <summary>When the first failed payment happened, UTC; the grace period counts from here.</summary>
    public DateTime? PastDueSince { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Set on every change. It is also a concurrency token, so two parallel updates cannot both apply the same transition and notify twice.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
