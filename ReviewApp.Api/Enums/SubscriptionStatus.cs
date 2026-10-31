namespace ReviewApp.Api.Enums;

public enum SubscriptionStatus
{
    None,    // row exists (checkout was started) but nothing was ever paid
    Active,  // paid; with UserSubscription.CancelAtPeriodEnd it is cancelled but runs until the period end
    PastDue, // a renewal payment failed; access continues for a grace period
    Ended    // over, the user is Free again
}
