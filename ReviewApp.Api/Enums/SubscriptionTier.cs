namespace ReviewApp.Api.Enums;

/// <summary>
/// The plan a user can use right now. Ordered, because feature access is a minimum-tier comparison
/// (<c>tier &gt;= feature.MinimumTier</c>): a higher tier includes everything below it.
/// </summary>
public enum SubscriptionTier
{
    Free = 0,
    Premium = 1
}
