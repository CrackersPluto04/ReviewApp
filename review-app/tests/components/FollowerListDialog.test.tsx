import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { FollowerListDialog } from '../../src/components/FollowerListDialog';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser, compactUser } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubAlert, stubConfirm } from '../support/browser';
import { knownIssue } from '../support/known-issue';

type Props = Parameters<typeof FollowerListDialog>[0];

function serveList(type: 'followers' | 'following', users: ReturnType<typeof compactUser>[] | Response) {
    const endpoint = capture('get', `User/:username/${type}`, users instanceof Response ? users : HttpResponse.json(users));
    server.use(endpoint.handler);
    return endpoint;
}

function renderList(props: Partial<Props> = {}, user: ReturnType<typeof authUser> | null = authUser({ username: 'alice' })) {
    const handlers = { onClose: vi.fn(), onFollowerRemoved: vi.fn(), onFollowingCountChanged: vi.fn() };
    const result = renderWithApp(<FollowerListDialog open username="alice" type="followers" {...handlers} {...props} />, { user });
    return { ...result, ...handlers };
}

const row = (username: string) => screen.getByText(username).closest('li') as HTMLElement;

describe('FollowerListDialog', () => {
    it('lists a user\'s followers', async () => {
        const list = serveList('followers', [compactUser({ username: 'bob' }), compactUser({ username: 'carol' })]);
        renderList({ username: 'b b' }, null);

        expect(screen.getByRole('dialog', { name: 'Followers' })).toBeInTheDocument();
        expect(await screen.findByText('bob')).toBeInTheDocument();
        expect(screen.getByText('carol')).toBeInTheDocument();
        expect(list.last.url.pathname).toBe('/api/User/b%20b/followers');
    });

    it('lists who a user follows', async () => {
        serveList('following', [compactUser({ username: 'dave' })]);
        renderList({ type: 'following' });

        expect(screen.getByRole('dialog', { name: 'Following' })).toBeInTheDocument();
        expect(await screen.findByText('dave')).toBeInTheDocument();
    });

    it('loads only when open', async () => {
        const list = serveList('followers', []);
        renderWithApp(<FollowerListDialog open={false} username="alice" type="followers" onClose={vi.fn()} />);

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(list.calls).toHaveLength(0);
    });

    it('says when the list is empty', async () => {
        serveList('following', []);
        renderList({ type: 'following' });

        expect(await screen.findByText('No following found.')).toBeInTheDocument();
    });

    it('shows a spinner, then the error', async () => {
        const response = deferred(() => apiError(500, 'Could not load followers.'));
        server.use(capture('get', 'User/:username/followers', () => response.promise).handler);
        renderList();

        expect(await screen.findByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('Could not load followers.')).toBeInTheDocument();
    });

    it('opens a user\'s profile and closes', async () => {
        serveList('followers', [compactUser({ username: 'b b' })]);
        const { user, onClose } = renderList();

        await user.click(await screen.findByText('b b'));

        expect(onClose).toHaveBeenCalled();
        expect(currentLocation()).toBe('/profile/b%20b/overview');
    });

    it('closes with the close button', async () => {
        serveList('followers', []);
        const { user, onClose } = renderList();
        await screen.findByText('No followers found.');

        await user.click(screen.getByTestId('CloseIcon').closest('button')!);

        expect(onClose).toHaveBeenCalled();
    });

    describe('following from the list', () => {
        it('follows and unfollows optimistically and updates the owner\'s count', async () => {
            serveList('following', [compactUser({ id: 21, username: 'bob', isFollowedByCurrentUser: true })]);
            const unfollow = capture('delete', 'Follower/21/unfollow', ok({ message: 'ok' }));
            const follow = capture('post', 'Follower/21/follow', ok({ message: 'ok' }));
            server.use(unfollow.handler, follow.handler);
            const { user, onFollowingCountChanged } = renderList({ type: 'following' });
            await screen.findByText('bob');

            await user.click(within(row('bob')).getByRole('button', { name: 'Unfollow' }));
            expect(within(row('bob')).getByRole('button', { name: 'Follow' })).toBeInTheDocument();
            await waitFor(() => expect(unfollow.calls).toHaveLength(1));

            await user.click(within(row('bob')).getByRole('button', { name: 'Follow' }));
            await waitFor(() => expect(follow.calls).toHaveLength(1));
            expect(onFollowingCountChanged.mock.calls).toEqual([[-1], [1]]);
        });

        it('rolls back and explains a failed follow', async () => {
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            server.use(capture('post', 'Follower/21/follow', apiError(400, 'You already follow this user.')).handler);
            const { user, onFollowingCountChanged } = renderList();
            await screen.findByText('bob');

            await user.click(within(row('bob')).getByRole('button', { name: 'Follow' }));

            expect(await screen.findByText('You already follow this user.')).toBeInTheDocument();
            expect(within(row('bob')).getByRole('button', { name: 'Follow' })).toBeInTheDocument();
            expect(onFollowingCountChanged.mock.calls).toEqual([[1], [-1]]);
        });

        it('leaves the counts alone on someone else\'s list', async () => {
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            server.use(capture('post', 'Follower/21/follow', ok({ message: 'ok' })).handler);
            const { user, onFollowingCountChanged } = renderList({ username: 'carol' });
            await screen.findByText('bob');

            await user.click(within(row('bob')).getByRole('button', { name: 'Follow' }));

            expect(onFollowingCountChanged).not.toHaveBeenCalled();
        });

        it('has no buttons on the user\'s own row', async () => {
            serveList('followers', [compactUser({ username: 'alice' })]);
            renderList({ username: 'carol' });

            await screen.findByText('alice');
            // Once the session is known
            await waitFor(() => expect(within(row('alice')).queryByRole('button')).not.toBeInTheDocument());
        });

        it('asks a visitor to log in', async () => {
            const alert = stubAlert();
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            const follow = capture('post', 'Follower/21/follow', ok({}));
            server.use(follow.handler);
            const { user } = renderList({}, null);
            await screen.findByText('bob');

            await user.click(within(row('bob')).getByRole('button', { name: 'Follow' }));

            expect(alert).toHaveBeenCalledWith('You must be logged in to do this.');
            expect(follow.calls).toHaveLength(0);
        });
    });

    describe('removing followers (own list)', () => {
        it('removes a follower after confirming', async () => {
            const confirm = stubConfirm(true);
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            const remove = capture('delete', 'Follower/21/remove', ok({ message: 'Removed.' }));
            server.use(remove.handler);
            const { user, onFollowerRemoved } = renderList();
            await screen.findByText('bob');

            await user.click(await within(row('bob')).findByRole('button', { name: 'Remove' }));

            expect(confirm).toHaveBeenCalledWith('Remove this follower?');
            await waitFor(() => expect(onFollowerRemoved).toHaveBeenCalled());
            expect(screen.queryByText('bob')).not.toBeInTheDocument();
        });

        it('keeps the follower when cancelled', async () => {
            stubConfirm(false);
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            const remove = capture('delete', 'Follower/21/remove', ok({}));
            server.use(remove.handler);
            const { user } = renderList();
            await screen.findByText('bob');

            await user.click(await within(row('bob')).findByRole('button', { name: 'Remove' }));

            expect(screen.getByText('bob')).toBeInTheDocument();
            expect(remove.calls).toHaveLength(0);
        });

        it('shows why removing failed', async () => {
            stubConfirm(true);
            serveList('followers', [compactUser({ id: 21, username: 'bob' })]);
            server.use(capture('delete', 'Follower/21/remove', apiError(500, 'Could not remove.')).handler);
            const { user, onFollowerRemoved } = renderList();
            await screen.findByText('bob');

            await user.click(await within(row('bob')).findByRole('button', { name: 'Remove' }));

            expect(await screen.findByText('Could not remove.')).toBeInTheDocument();
            expect(onFollowerRemoved).not.toHaveBeenCalled();
        });

        it('is not offered on the following list or other users\' lists', async () => {
            serveList('following', [compactUser({ username: 'bob' })]);
            renderList({ type: 'following' });
            await screen.findByText('bob');

            expect(within(row('bob')).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
        });
    });

    knownIssue('W18', 'an earlier error is gone when the list is opened again', async () => {
        let fail = true;
        server.use(capture('get', 'User/:username/followers', () => fail ? apiError(500, 'Could not load followers.') : HttpResponse.json([compactUser({ username: 'bob' })])).handler);
        const handlers = { onClose: vi.fn() };
        const { rerender } = renderWithApp(<FollowerListDialog open username="alice" type="followers" {...handlers} />);
        await screen.findByText('Could not load followers.');

        fail = false;
        rerender(<FollowerListDialog open={false} username="alice" type="followers" {...handlers} />);
        rerender(<FollowerListDialog open username="alice" type="followers" {...handlers} />);

        await screen.findByText('bob');
        expect(screen.queryByText('Could not load followers.')).not.toBeInTheDocument();
    });
});
