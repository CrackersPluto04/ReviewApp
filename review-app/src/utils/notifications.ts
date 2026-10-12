import { NotificationDto, NotificationType } from "../types/types";

export const NOTIFICATION_TYPE_LABELS: Record<NotificationType, string> = {
    System: 'System',
    ReviewReply: 'Reply',
    ReplyReply: 'Reply',
    AchievementUnlocked: 'Achievement',
    NewFollower: 'Follower'
};

// Where "take me there" leads, null when the notification has no source to open
export function getNotificationRoute(notification: NotificationDto, currentUsername?: string): string | null {
    switch (notification.type) {
        case 'AchievementUnlocked':
            return currentUsername ? `/profile/${encodeURIComponent(currentUsername)}/achievements` : null;

        case 'NewFollower':
            return notification.actorUsername ? `/profile/${encodeURIComponent(notification.actorUsername)}/overview` : null;

        case 'ReviewReply':
        case 'ReplyReply':
        case 'System':
        default:
            if (!notification.reviewID) return null;
            return notification.replyID
                ? `/review/${notification.reviewID}?reply=${notification.replyID}`
                : `/review/${notification.reviewID}`;
    }
}

// Stored in UTC, shown in the viewer's local time
export function formatNotificationDate(isoDate: string): string {
    return new Date(isoDate).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}
