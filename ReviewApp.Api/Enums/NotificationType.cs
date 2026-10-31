namespace ReviewApp.Api.Enums;

public enum NotificationType
{
    System,
    ReviewReply,         // someone replied to the recipient's review
    ReplyReply,          // someone replied to the recipient's reply
    AchievementUnlocked, // one notification per unlocked tier
    NewFollower,
    SubscriptionStarted,
    PaymentFailed,       // a renewal payment failed, Premium continues for a grace period
    SubscriptionCancelled,
    SubscriptionEnded
}
