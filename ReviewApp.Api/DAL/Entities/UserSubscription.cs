using System.ComponentModel.DataAnnotations;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

// One row per user who ever started a checkout; no row means Free.
public class UserSubscription
{
    public int ID { get; set; }
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Premium; // the plan this subscription is for
    public SubscriptionProvider Provider { get; set; }

    [MaxLength(100)]
    public string ProviderCustomerID { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ProviderSubscriptionID { get; set; } // null until the first payment went through

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.None;
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? PastDueSince { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
