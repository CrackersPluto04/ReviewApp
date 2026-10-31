import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { NotificationCard } from '../../src/components/NotificationCard';
import { NotificationType } from '../../src/types/types';
import { notification } from '../support/builders';
import { setupUser } from '../support/render';

function renderCard(overrides: Parameters<typeof notification>[0] = {}, hasSource = true) {
    const handlers = { onToggleSeen: vi.fn(), onDelete: vi.fn(), onOpen: vi.fn() };
    const item = notification(overrides);
    render(<ul><NotificationCard notification={item} hasSource={hasSource} {...handlers} /></ul>);
    return { ...handlers, item, user: setupUser() };
}

describe('NotificationCard', () => {
    it('shows an unseen notification with its type, date and plain-text message', () => {
        renderCard({ title: 'New reply', message: '<b>carol</b>: Nice!', type: 'ReviewReply', createdAt: '2026-04-01T14:05:00Z' });

        expect(screen.getByRole('listitem', { name: 'Unseen notification: New reply' })).toBeInTheDocument();
        expect(screen.getByText('Reply · Apr 1, 2026, 2:05 PM')).toBeInTheDocument();
        expect(screen.getByText('<b>carol</b>: Nice!')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Mark as seen' })).toBeInTheDocument();
    });

    it('labels a seen notification and offers to mark it unseen', () => {
        renderCard({ title: 'Old', isSeen: true });

        expect(screen.getByRole('listitem', { name: 'Seen notification: Old' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Mark as unseen' })).toBeInTheDocument();
    });

    it('reports the actions', async () => {
        const { user, item, onOpen, onToggleSeen, onDelete } = renderCard();

        await user.click(screen.getByRole('button', { name: 'Take me there' }));
        await user.click(screen.getByRole('button', { name: 'Mark as seen' }));
        await user.click(screen.getByRole('button', { name: 'Delete notification' }));

        expect(onOpen).toHaveBeenCalledWith(item);
        expect(onToggleSeen).toHaveBeenCalledWith(item);
        expect(onDelete).toHaveBeenCalledWith(item);
    });

    it('has no "Take me there" without a source', () => {
        renderCard({}, false);
        expect(screen.queryByRole('button', { name: 'Take me there' })).not.toBeInTheDocument();
    });

    it('shows the actor\'s avatar when a person triggered it', () => {
        renderCard({ type: 'NewFollower', actorUsername: 'bob', actorProfilePictureUrl: 'https://img.example/bob.png' });

        expect(screen.getByRole('img', { name: 'bob' })).toHaveAttribute('src', 'https://img.example/bob.png');
    });

    it.each([
        ['AchievementUnlocked', 'EmojiEventsIcon'],
        ['ReviewReply', 'ChatBubbleOutlineIcon'],
        ['NewFollower', 'PersonAddAlt1Icon'],
        ['SubscriptionStarted', 'WorkspacePremiumIcon'],
        ['PaymentFailed', 'CreditCardOffOutlinedIcon'],
        ['System', 'InfoOutlinedIcon'],
        ['SomethingNew', 'InfoOutlinedIcon'],
    ])('shows a %s icon without an actor', (type, icon) => {
        renderCard({ type: type as NotificationType });
        expect(screen.getByTestId(icon)).toBeInTheDocument();
    });

    it('shows an unknown type by its name', () => {
        renderCard({ type: 'SomethingNew' as NotificationType, createdAt: '2026-04-01T14:05:00Z' });
        expect(screen.getByText('SomethingNew · Apr 1, 2026, 2:05 PM')).toBeInTheDocument();
    });
});
