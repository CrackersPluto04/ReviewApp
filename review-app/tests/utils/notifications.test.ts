import { describe, expect, it } from 'vitest';
import { formatNotificationDate, getNotificationRoute, NOTIFICATION_TYPE_LABELS } from '../../src/utils/notifications';
import { NotificationType } from '../../src/types/types';
import { notification } from '../support/builders';

const ALL_TYPES: NotificationType[] = ['System', 'ReviewReply', 'ReplyReply', 'AchievementUnlocked', 'NewFollower',
    'SubscriptionStarted', 'PaymentFailed', 'SubscriptionCancelled', 'SubscriptionEnded'];

describe('notifications utils', () => {
    it('has a label for every type', () => {
        expect(Object.keys(NOTIFICATION_TYPE_LABELS).sort()).toEqual([...ALL_TYPES].sort());
        expect(NOTIFICATION_TYPE_LABELS.ReviewReply).toBe('Reply');
        expect(NOTIFICATION_TYPE_LABELS.ReplyReply).toBe('Reply');
        expect(NOTIFICATION_TYPE_LABELS.PaymentFailed).toBe('Premium');
    });

    describe('getNotificationRoute', () => {
        it.each(['ReviewReply', 'ReplyReply', 'System'] as NotificationType[])('%s opens the review, at the reply when there is one', type => {
            expect(getNotificationRoute(notification({ type, reviewID: 12, replyID: 34 }))).toBe('/review/12?reply=34');
            expect(getNotificationRoute(notification({ type, reviewID: 12 }))).toBe('/review/12');
            expect(getNotificationRoute(notification({ type }))).toBeNull();
        });

        it('opens the current user\'s achievements for an unlock', () => {
            const unlock = notification({ type: 'AchievementUnlocked', groupCode: 'CRITIC' });

            expect(getNotificationRoute(unlock, 'alice')).toBe('/profile/alice/achievements');
            expect(getNotificationRoute(unlock, 'a b/c')).toBe('/profile/a%20b%2Fc/achievements');
            expect(getNotificationRoute(unlock)).toBeNull();
        });

        it('opens the follower\'s profile for a new follower', () => {
            expect(getNotificationRoute(notification({ type: 'NewFollower', actorUsername: 'bob' }))).toBe('/profile/bob/overview');
            expect(getNotificationRoute(notification({ type: 'NewFollower', actorUsername: 'b&b' }))).toBe('/profile/b%26b/overview');
            expect(getNotificationRoute(notification({ type: 'NewFollower', actorUsername: null }))).toBeNull();
        });

        it.each(['SubscriptionStarted', 'PaymentFailed', 'SubscriptionCancelled', 'SubscriptionEnded'] as NotificationType[])(
            '%s opens the Premium page', type => {
                expect(getNotificationRoute(notification({ type, reviewID: 5 }))).toBe('/premium');
            });

        it('treats an unknown type like a review notification', () => {
            expect(getNotificationRoute(notification({ type: 'SomethingNew' as NotificationType, reviewID: 7 }))).toBe('/review/7');
        });
    });

    it('formats the UTC date in the viewer\'s locale and time zone (pinned to en-US / UTC)', () => {
        expect(formatNotificationDate('2026-04-01T14:05:00Z')).toBe('Apr 1, 2026, 2:05 PM');
    });
});
