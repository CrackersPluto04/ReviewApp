using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

public class UserSubscription
{
    public int ID { get; set; }
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
    public string ExternalSubscriptionID { get; set; } = string.Empty; // Paypal / Stripe ID
    public string Status { get; set; } = string.Empty; // Active / Cancelled / Expired
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
