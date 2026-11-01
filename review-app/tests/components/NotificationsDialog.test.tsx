import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { NotificationsDialog } from '../../src/components/NotificationsDialog';
import { server } from '../support/server';
import { apiError, capture, deferred, noContent, ok } from '../support/api';
import { authUser, notification, notificationPage } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubMatchMedia } from '../support/browser';

type Item = ReturnType<typeof notification>;

function serveList(respond: (query: URLSearchParams) => Response | Promise<Response>) {
    const endpoint = capture('get', 'Notification', req => respond(req.url.searchParams));
    server.use(endpoint.handler);
    return endpoint;
}

const page = (items: Item[], overrides: Record<string, any> = {}) => HttpResponse.json(notificationPage(items, overrides));

function serveCount(count: number) {
    server.use(capture('get', 'Notification/unseen-count', ok({ count })).handler);
}

function renderDialog(onClose = vi.fn()) {
    const result = renderWithApp(<NotificationsDialog open onClose={onClose} />, { user: authUser({ username: 'alice' }) });
    return { ...result, onClose };
}

const card = (title: string) => screen.getByRole('listitem', { name: new RegExp(`notification: ${title}$`) });
const badgeText = () => screen.getByText(/unseen$|You are all caught up/);

describe('NotificationsDialog', () => {
    it('loads the newest notifications of every status', async () => {
        serveCount(1);
        const list = serveList(() => page([notification({ title: 'First' }), notification({ title: 'Second', isSeen: true })], { totalCount: 2, unseenCount: 1 }));
        renderDialog();

        expect(await screen.findByText('First')).toBeInTheDocument();
        expect(screen.getByRole('dialog', { name: /Notifications \(2\)/ })).toBeInTheDocument();
        expect(badgeText()).toHaveTextContent('1 unseen');
        expect(Object.fromEntries(list.last.url.searchParams)).toEqual({ status: 'all', sort: 'newest', pageSize: '20' });
    });

    it('says when there are no notifications, per filter', async () => {
        serveCount(0);
        serveList(() => page([]));
        const { user } = renderDialog();

        expect(await screen.findByText('You have no notifications yet.')).toBeInTheDocument();
        expect(badgeText()).toHaveTextContent('You are all caught up');

        await user.click(screen.getByRole('button', { name: 'Unseen' }));
        expect(await screen.findByText('No unseen notifications.')).toBeInTheDocument();
    });

    it('filters with the toggles: one = that status, both or none = all', async () => {
        serveCount(0);
        const list = serveList(() => page([]));
        const { user } = renderDialog();
        await screen.findByText('You have no notifications yet.');

        await user.click(screen.getByRole('button', { name: 'Seen' }));
        await waitFor(() => expect(list.last.url.searchParams.get('status')).toBe('seen'));
        await user.click(screen.getByRole('button', { name: 'Unseen' }));
        await waitFor(() => expect(list.last.url.searchParams.get('status')).toBe('all'));
        await user.click(screen.getByRole('button', { name: 'Seen' }));
        await waitFor(() => expect(list.last.url.searchParams.get('status')).toBe('unseen'));
    });

    it('sorts oldest first', async () => {
        serveCount(0);
        const list = serveList(() => page([]));
        const { user } = renderDialog();
        await screen.findByText('You have no notifications yet.');

        await user.click(screen.getByRole('combobox', { name: 'Sort by' }));
        await user.click(screen.getByRole('option', { name: 'Oldest first' }));

        await waitFor(() => expect(list.last.url.searchParams.get('sort')).toBe('oldest'));
    });

    it('loads more after the last notification, without duplicates', async () => {
        serveCount(0);
        const list = serveList(query => query.has('afterId')
            ? page([notification({ id: 2, title: 'Two again' }), notification({ id: 3, title: 'Three' })])
            : page([notification({ id: 1, title: 'One' }), notification({ id: 2, title: 'Two' })], { hasMore: true, totalCount: 3 }));
        const { user } = renderDialog();
        await screen.findByText('Two');

        await user.click(screen.getByRole('button', { name: 'Load more' }));

        expect(await screen.findByText('Three')).toBeInTheDocument();
        expect(screen.queryByText('Two again')).not.toBeInTheDocument();
        expect(list.last.url.searchParams.get('afterId')).toBe('2');
    });

    it('drops the answer of a filter the user already left', async () => {
        serveCount(0);
        const slow = deferred(() => page([notification({ title: 'Old filter result' })]));
        serveList(query => query.get('status') === 'all' ? slow.promise : page([notification({ title: 'Seen result', isSeen: true })]));
        const { user } = renderDialog();

        await user.click(screen.getByRole('button', { name: 'Seen' }));
        expect(await screen.findByText('Seen result')).toBeInTheDocument();
        slow.release();

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(screen.queryByText('Old filter result')).not.toBeInTheDocument();
    });

    it('shows the loading error', async () => {
        serveCount(0);
        serveList(() => apiError(500, 'Could not load your notifications.'));
        renderDialog();

        expect(await screen.findByText('Could not load your notifications.')).toBeInTheDocument();
        expect(screen.queryByText('You have no notifications yet.')).not.toBeInTheDocument();
    });

    describe('actions', () => {
        it('marks one seen and unseen, with the badge', async () => {
            serveCount(1);
            serveList(() => page([notification({ id: 5, title: 'Hello' })], { unseenCount: 1 }));
            const seen = capture('patch', 'Notification/5/seen', noContent());
            server.use(seen.handler);
            const { user } = renderDialog();
            await screen.findByText('Hello');

            await user.click(within(card('Hello')).getByRole('button', { name: 'Mark as seen' }));
            expect(card('Hello')).toHaveAccessibleName('Seen notification: Hello');
            expect(badgeText()).toHaveTextContent('You are all caught up');
            await waitFor(() => expect(seen.last.body).toEqual({ isSeen: true }));

            await user.click(within(card('Hello')).getByRole('button', { name: 'Mark as unseen' }));
            expect(badgeText()).toHaveTextContent('1 unseen');
            await waitFor(() => expect(seen.last.body).toEqual({ isSeen: false }));
        });

        it('rolls a failed change back and explains it', async () => {
            serveCount(1);
            serveList(() => page([notification({ id: 5, title: 'Hello' })], { unseenCount: 1 }));
            server.use(capture('patch', 'Notification/5/seen', apiError(500, 'Could not update the notification.')).handler);
            const { user } = renderDialog();
            await screen.findByText('Hello');

            await user.click(within(card('Hello')).getByRole('button', { name: 'Mark as seen' }));

            expect(await screen.findByText('Could not update the notification.')).toBeInTheDocument();
            expect(card('Hello')).toHaveAccessibleName('Unseen notification: Hello');
            expect(badgeText()).toHaveTextContent('1 unseen');
        });

        it('deletes one, and puts it back in place when that fails', async () => {
            // The provider's own count agrees with the list's
            serveCount(2);
            serveList(() => page([notification({ id: 1, title: 'A' }), notification({ id: 2, title: 'B', isSeen: true }), notification({ id: 3, title: 'C' })], { totalCount: 3, unseenCount: 2 }));
            server.use(
                capture('delete', 'Notification/1', noContent()).handler,
                capture('delete', 'Notification/2', apiError(500, 'Could not delete it.')).handler,
            );
            const { user } = renderDialog();
            await screen.findByText('A');

            await user.click(within(card('A')).getByRole('button', { name: 'Delete notification' }));
            await waitFor(() => expect(screen.queryByText('A')).not.toBeInTheDocument());
            expect(screen.getByRole('dialog', { name: /Notifications \(2\)/ })).toBeInTheDocument();
            expect(badgeText()).toHaveTextContent('1 unseen');

            await user.click(within(card('B')).getByRole('button', { name: 'Delete notification' }));
            expect(await screen.findByText('Could not delete it.')).toBeInTheDocument();
            expect(screen.getAllByRole('listitem').map(li => li.getAttribute('aria-label'))).toEqual([
                'Seen notification: B', 'Unseen notification: C',
            ]);
        });

        it('marks all seen, and reloads a filtered list', async () => {
            serveCount(2);
            const list = serveList(query => query.get('status') === 'unseen'
                ? page(query.has('afterId') ? [] : [notification({ title: 'Unseen one' })], { unseenCount: 2 })
                : page([notification({ title: 'Unseen one' })], { unseenCount: 2 }));
            const markAll = capture('post', 'Notification/seen-all', noContent());
            server.use(markAll.handler);
            const { user } = renderDialog();
            await screen.findByText('Unseen one');
            await user.click(screen.getByRole('button', { name: 'Unseen' }));
            await waitFor(() => expect(list.last.url.searchParams.get('status')).toBe('unseen'));
            const before = list.calls.length;

            await user.click(screen.getByRole('button', { name: 'Mark all as seen' }));

            await waitFor(() => expect(markAll.calls).toHaveLength(1));
            await waitFor(() => expect(list.calls.length).toBe(before + 1));
        });

        it('rolls back a failed "mark all seen"', async () => {
            serveCount(1);
            serveList(() => page([notification({ title: 'Hello' })], { unseenCount: 1 }));
            server.use(capture('post', 'Notification/seen-all', apiError(500, 'Could not mark them seen.')).handler);
            const { user } = renderDialog();
            await screen.findByText('Hello');

            await user.click(screen.getByRole('button', { name: 'Mark all as seen' }));

            expect(await screen.findByText('Could not mark them seen.')).toBeInTheDocument();
            expect(card('Hello')).toHaveAccessibleName('Unseen notification: Hello');
            expect(badgeText()).toHaveTextContent('1 unseen');
        });

        it('disables "mark all seen" when everything is seen', async () => {
            serveCount(0);
            serveList(() => page([notification({ isSeen: true })], { unseenCount: 0 }));
            renderDialog();

            await waitFor(() => expect(screen.getByRole('button', { name: 'Mark all as seen' })).toBeDisabled());
        });

        it('deletes the seen ones after confirming', async () => {
            serveCount(0);
            let cleared = false;
            const list = serveList(() => page(cleared ? [] : [notification({ title: 'Seen one', isSeen: true })]));
            const clear = capture('delete', 'Notification/seen', () => { cleared = true; return noContent(); });
            server.use(clear.handler);
            const { user } = renderDialog();
            await screen.findByText('Seen one');

            await user.click(screen.getByRole('button', { name: 'Delete all seen notifications' }));
            expect(screen.getByText('Delete every seen notification? This cannot be undone.')).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: 'Delete' }));

            expect(await screen.findByText('You have no notifications yet.')).toBeInTheDocument();
            expect(clear.calls).toHaveLength(1);
            expect(list.calls.length).toBeGreaterThanOrEqual(2);
        });

        it('keeps everything when the clearing is cancelled or fails', async () => {
            serveCount(0);
            serveList(() => page([notification({ title: 'Seen one', isSeen: true })]));
            const clear = capture('delete', 'Notification/seen', apiError(500, 'Could not clear them.'));
            server.use(clear.handler);
            const { user } = renderDialog();
            await screen.findByText('Seen one');

            await user.click(screen.getByRole('button', { name: 'Delete all seen notifications' }));
            await user.click(screen.getByRole('button', { name: 'Cancel' }));
            expect(screen.queryByText(/Delete every seen notification/)).not.toBeInTheDocument();
            expect(clear.calls).toHaveLength(0);

            await user.click(screen.getByRole('button', { name: 'Delete all seen notifications' }));
            await user.click(screen.getByRole('button', { name: 'Delete' }));
            expect(await screen.findByText('Could not clear them.')).toBeInTheDocument();
            expect(screen.getByText('Seen one')).toBeInTheDocument();
        });

        it('"Take me there" marks it seen, closes and navigates', async () => {
            serveCount(1);
            serveList(() => page([notification({ id: 5, title: 'New reply', type: 'ReviewReply', reviewID: 12, replyID: 34 })], { unseenCount: 1 }));
            const seen = capture('patch', 'Notification/5/seen', noContent());
            server.use(seen.handler);
            const { user, onClose } = renderDialog();
            await screen.findByText('New reply');

            await user.click(screen.getByRole('button', { name: 'Take me there' }));

            expect(onClose).toHaveBeenCalled();
            expect(currentLocation()).toBe('/review/12?reply=34');
            await waitFor(() => expect(seen.last.body).toEqual({ isSeen: true }));
        });

        it('does not mark an already seen one again', async () => {
            serveCount(0);
            serveList(() => page([notification({ title: 'Unlocked', type: 'AchievementUnlocked', isSeen: true })]));
            const seen = capture('patch', 'Notification/:id/seen', noContent());
            server.use(seen.handler);
            const { user } = renderDialog();
            await screen.findByText('Unlocked');

            // The route needs the logged-in username
            await user.click(await screen.findByRole('button', { name: 'Take me there' }));

            expect(currentLocation()).toBe('/profile/alice/achievements');
            await new Promise(resolve => setTimeout(resolve, 50));
            expect(seen.calls).toHaveLength(0);
        });
    });

    it('is full screen with a close button on small screens', async () => {
        stubMatchMedia(query => query.includes('max-width'));
        serveCount(0);
        serveList(() => page([]));
        const { user, onClose } = renderDialog();
        await screen.findByText('You have no notifications yet.');

        await user.click(screen.getByRole('button', { name: 'Close' }));

        expect(onClose).toHaveBeenCalled();
        expect(document.querySelector('.MuiDialog-paperFullScreen')).toBeInTheDocument();
    });

    it('closes from the title bar', async () => {
        serveCount(0);
        serveList(() => page([]));
        const { user, onClose } = renderDialog();

        await user.click(screen.getByRole('button', { name: 'Close notifications' }));

        expect(onClose).toHaveBeenCalled();
        expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    });
});
