import { NotificationDto, NotificationType } from "../types/types";

/** The short label of the type chip on a notification card. Every Premium related type shares one label. */
export const NOTIFICATION_TYPE_LABELS: Record<NotificationType, string> = {
    System: 'System',
    ReviewReply: 'Reply',
    ReplyReply: 'Reply',
    AchievementUnlocked: 'Achievement',
    NewFollower: 'Follower',
    SubscriptionStarted: 'Premium',
    PaymentFailed: 'Premium',
    SubscriptionCancelled: 'Premium',
    SubscriptionEnded: 'Premium'
};

/**
 * Where "take me there" leads. A notification stores only plain IDs and names (no foreign keys), so it can outlive
 * the content it points at: the target page then shows its own not-found state. A new `NotificationType` needs a
 * case here.
 * @param notification The notification.
 * @param currentUsername The logged-in user's username; the achievements link needs it.
 * @returns The app route, or `null` when the notification has no source to open.
 */
export function getNotificationRoute(notification: NotificationDto, currentUsername?: string): string | null {
    switch (notification.type) {
        case 'AchievementUnlocked':
            return currentUsername ? `/profile/${encodeURIComponent(currentUsername)}/achievements` : null;

        case 'NewFollower':
            return notification.actorUsername ? `/profile/${encodeURIComponent(notification.actorUsername)}/overview` : null;

        // The plans page shows the subscription and its actions
        case 'SubscriptionStarted':
        case 'PaymentFailed':
        case 'SubscriptionCancelled':
        case 'SubscriptionEnded':
            return '/premium';

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

/**
 * Formats a notification's time. Stored in UTC, shown in the viewer's local time and locale.
 * @param isoDate The ISO 8601 timestamp from the API.
 * @returns A medium date with a short time.
 */
export function formatNotificationDate(isoDate: string): string {
    return new Date(isoDate).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}
