namespace ReviewApp.Api.Enums;

public enum NotificationType
{
    System,
    ReviewReply,         // someone replied to the recipient's review
    ReplyReply,          // someone replied to the recipient's reply
    AchievementUnlocked, // one notification per unlocked tier
    NewFollower
}
