namespace ReviewApp.Api.Enums;

/// <summary>
/// The kinds of notification. A new type needs a creation call where its data changes and a route in the clients'
/// notification mapping; clients show unknown names as <see cref="NotificationType.System"/>. The subscription types route to the
/// Premium page.
/// </summary>
public enum NotificationType
{
    System,
    /// <summary>Someone replied to the recipient's review.</summary>
    ReviewReply,
    /// <summary>Someone replied to the recipient's reply.</summary>
    ReplyReply,
    /// <summary>One notification per unlocked tier.</summary>
    AchievementUnlocked,
    NewFollower,
    SubscriptionStarted,
    /// <summary>A renewal payment failed; Premium continues for a grace period.</summary>
    PaymentFailed,
    SubscriptionCancelled,
    SubscriptionEnded
}
