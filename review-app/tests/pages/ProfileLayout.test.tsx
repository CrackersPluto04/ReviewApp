import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser, userProfile } from '../support/builders';
import { currentLocation, renderApp } from '../support/render';
import { stubAlert } from '../support/browser';
import { knownIssue } from '../support/known-issue';

function serveProfiles(byName: Record<string, ReturnType<typeof userProfile>>) {
    const endpoint = capture('get', 'User/:username', (_req, params) => {
        const profile = byName[params.username as string];
        return profile ? HttpResponse.json(profile) : HttpResponse.json({ error: 'User not found.' }, { status: 404 });
    });
    server.use(endpoint.handler);
    return endpoint;
}

const header = () => screen.getByRole('main');
const alice = authUser({ id: 1, username: 'alice' });

// The owner's controls show once the session is known; that also refetches the profile (W16), so wait until it settles
async function ownerReady() {
    await screen.findByTestId('ManageAccountsIcon');
    await new Promise(resolve => setTimeout(resolve, 100));
    await waitFor(() => {
        expect(screen.getByTestId('ManageAccountsIcon')).toBeInTheDocument();
        expect(within(header()).queryByRole('progressbar')).not.toBeInTheDocument();
    });
}

describe('ProfileLayout', () => {
    it('shows a profile with its counts, bio and tabs', async () => {
        const profiles = serveProfiles({ bob: userProfile({ username: 'bob', bio: 'Film nerd.', createdAt: '2026-01-15', followersCount: 12, followingCount: 3 }) });
        renderApp('/profile/bob/overview');

        expect(await within(header()).findByRole('heading', { name: 'bob' })).toBeInTheDocument();
        expect(screen.getByText('Joined 2026-01-15')).toBeInTheDocument();
        expect(screen.getByText('Film nerd.')).toBeInTheDocument();
        expect(screen.getByText('12')).toBeInTheDocument();
        expect(screen.getByText('3')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByText('Overview Tab (Coming Soon)')).toBeInTheDocument();
        expect(profiles.last.url.pathname).toBe('/api/User/bob');
    });

    it('opens the overview for /profile/:username', async () => {
        serveProfiles({ bob: userProfile({ username: 'bob' }) });
        renderApp('/profile/bob');

        await screen.findByText('Overview Tab (Coming Soon)');
        expect(currentLocation()).toBe('/profile/bob/overview');
    });

    it('welcomes on an empty bio and shows the Premium badge', async () => {
        serveProfiles({ bob: userProfile({ username: 'bob', bio: undefined, isPremium: true }) });
        renderApp('/profile/bob/overview');

        expect(await screen.findByText("Welcome to bob's profile!")).toBeInTheDocument();
        expect(screen.getByLabelText('Premium member')).toBeInTheDocument();
    });

    it('switches tabs through the URL', async () => {
        serveProfiles({ bob: userProfile({ username: 'bob' }) });
        server.use(capture('get', 'Achievement/bob', ok([])).handler);
        const { user } = renderApp('/profile/bob/overview');
        await screen.findByText('Overview Tab (Coming Soon)');

        await user.click(screen.getByRole('tab', { name: 'Achievements' }));

        expect(currentLocation()).toBe('/profile/bob/achievements');
        expect(screen.getByRole('tab', { name: 'Achievements' })).toHaveAttribute('aria-selected', 'true');
    });

    it('shows the error for an unknown user', async () => {
        serveProfiles({});
        renderApp('/profile/nobody/overview');

        expect(await screen.findByText('User not found.')).toBeInTheDocument();
    });

    it('shows a spinner while loading', async () => {
        const response = deferred(() => HttpResponse.json(userProfile({ username: 'bob' })));
        server.use(capture('get', 'User/:username', () => response.promise).handler);
        renderApp('/profile/bob/overview');

        expect(await within(header()).findByRole('progressbar')).toBeInTheDocument();
        response.release();
        await within(header()).findByRole('heading', { name: 'bob' });
    });

    describe('visitor', () => {
        it('follows optimistically', async () => {
            serveProfiles({ bob: userProfile({ id: 7, username: 'bob', followersCount: 1 }) });
            const follow = capture('post', 'Follower/7/follow', ok({ message: 'ok' }));
            server.use(follow.handler);
            const { user } = renderApp('/profile/bob/overview', { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Follow' }));

            expect(screen.getByRole('button', { name: 'Unfollow' })).toBeInTheDocument();
            expect(screen.getByText('2')).toBeInTheDocument();
            await waitFor(() => expect(follow.calls).toHaveLength(1));
        });

        it('unfollows', async () => {
            serveProfiles({ bob: userProfile({ id: 7, username: 'bob', followersCount: 5, isFollowedByCurrentUser: true }) });
            server.use(capture('delete', 'Follower/7/unfollow', ok({ message: 'ok' })).handler);
            const { user } = renderApp('/profile/bob/overview', { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Unfollow' }));

            expect(await screen.findByRole('button', { name: 'Follow' })).toBeInTheDocument();
            expect(screen.getByText('4')).toBeInTheDocument();
        });

        it('asks an anonymous visitor to log in to follow', async () => {
            const alert = stubAlert();
            serveProfiles({ bob: userProfile({ id: 7, username: 'bob' }) });
            const { user } = renderApp('/profile/bob/overview', { user: null });

            await user.click(await screen.findByRole('button', { name: 'Follow' }));

            expect(alert).toHaveBeenCalledWith('You must be logged in to follow users.');
        });

        it('has no owner controls', async () => {
            serveProfiles({ bob: userProfile({ username: 'bob' }) });
            renderApp('/profile/bob/overview', { user: alice });

            await screen.findByRole('button', { name: 'Follow' });
            expect(screen.queryByTestId('ManageAccountsIcon')).not.toBeInTheDocument();
            expect(screen.queryByTestId('EditIcon')).not.toBeInTheDocument();
        });

        knownIssue('W15', 'a failed follow is rolled back and explained, the profile stays', async () => {
            serveProfiles({ bob: userProfile({ id: 7, username: 'bob', followersCount: 1 }) });
            server.use(capture('post', 'Follower/7/follow', apiError(400, 'You already follow this user.')).handler);
            const { user } = renderApp('/profile/bob/overview', { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Follow' }));

            expect(await screen.findByText('You already follow this user.')).toBeInTheDocument();
            // The whole profile is replaced by the message
            expect(within(header()).getByRole('heading', { name: 'bob' })).toBeInTheDocument();
        });
    });

    describe('follower lists', () => {
        it('opens the followers and following dialogs from the counts', async () => {
            serveProfiles({ bob: userProfile({ username: 'bob' }) });
            server.use(
                capture('get', 'User/:username/followers', ok([])).handler,
                capture('get', 'User/:username/following', ok([])).handler,
            );
            const { user } = renderApp('/profile/bob/overview');
            await within(header()).findByRole('heading', { name: 'bob' });

            await user.click(screen.getByText('Followers'));
            expect(await screen.findByRole('dialog', { name: 'Followers' })).toBeInTheDocument();
            await user.keyboard('{Escape}');
            await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

            await user.click(screen.getByText('Following'));
            expect(await screen.findByRole('dialog', { name: 'Following' })).toBeInTheDocument();
        });
    });

    describe('owner', () => {
        const mine = () => userProfile({ id: 1, username: 'alice', bio: 'Old bio', followersCount: 2, followingCount: 4 });

        it('edits the bio', async () => {
            serveProfiles({ alice: mine() });
            const update = capture('patch', 'User/me', ok({ message: 'Profile updated.' }));
            server.use(update.handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();

            // The bio's edit button is the second edit icon (the first is on the avatar)
            await user.click(screen.getAllByTestId('EditIcon')[1].closest('button')!);
            const box = screen.getByPlaceholderText('Write something about yourself...');
            expect(box).toHaveValue('Old bio');
            await user.clear(box);
            await user.type(box, 'New bio');
            await user.click(screen.getByRole('button', { name: 'Save' }));

            expect(await screen.findByText('New bio')).toBeInTheDocument();
            expect(update.last.body).toEqual({ bio: 'New bio' });
        });

        it('limits the bio to 150 characters and cancels', async () => {
            serveProfiles({ alice: mine() });
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();
            await user.click(screen.getAllByTestId('EditIcon')[1].closest('button')!);

            const box = screen.getByPlaceholderText('Write something about yourself...');
            await user.clear(box);
            await user.click(box);
            await user.paste('x'.repeat(151));
            expect(screen.getByText('151 / 150')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

            await user.click(screen.getByRole('button', { name: 'Cancel' }));
            expect(screen.getByText('Old bio')).toBeInTheDocument();
        });

        it('changes the avatar', async () => {
            serveProfiles({ alice: mine() });
            const update = capture('patch', 'User/me', ok({ message: 'Profile updated.' }));
            server.use(update.handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();

            await user.click(screen.getAllByTestId('EditIcon')[0].closest('button')!);
            const dialog = await screen.findByRole('dialog', { name: 'Update Profile Picture' });
            await user.type(within(dialog).getByLabelText('Image URL'), 'https://img.example/new.png');
            await user.click(within(dialog).getByRole('button', { name: 'Save' }));

            await waitFor(() => expect(update.last.body).toEqual({ profilePictureUrl: 'https://img.example/new.png' }));
            await waitFor(() => expect(screen.getByRole('button', { name: 'Account menu' }).querySelector('img')).toHaveAttribute('src', 'https://img.example/new.png'));
        });

        it('changes the username and stays on the same tab', async () => {
            const profiles = serveProfiles({ alice: mine(), alicia: userProfile({ id: 1, username: 'alicia' }) });
            server.use(capture('patch', 'User/me', ok({ message: 'Username changed.' })).handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();

            await user.click(screen.getByTestId('ManageAccountsIcon').closest('button')!);
            await user.click(screen.getByRole('menuitem', { name: 'Change username' }));
            const dialog = await screen.findByRole('dialog', { name: 'Change Username' });
            const field = within(dialog).getByLabelText('New username');
            // The dialog resets its draft in an effect after opening
            await new Promise(resolve => setTimeout(resolve, 100));
            expect(field).toHaveValue('alice');
            await user.clear(field);
            await user.type(field, 'alicia');
            await user.click(within(dialog).getByRole('button', { name: 'Save' }));

            await waitFor(() => expect(currentLocation()).toBe('/profile/alicia/overview'));
            // Once the dialog has closed and the profile of the new name loaded
            await waitFor(() => expect(within(header()).getByRole('heading', { name: 'alicia' })).toBeInTheDocument());
            expect(profiles.calls.map(c => c.url.pathname)).toContain('/api/User/alicia');
        });

        it('keeps a failed username change in the dialog', async () => {
            serveProfiles({ alice: mine() });
            server.use(capture('patch', 'User/me', apiError(409, 'This username is taken.')).handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();

            await user.click(screen.getByTestId('ManageAccountsIcon').closest('button')!);
            await user.click(screen.getByRole('menuitem', { name: 'Change username' }));
            const dialog = await screen.findByRole('dialog', { name: 'Change Username' });
            const field = within(dialog).getByLabelText('New username');
            await new Promise(resolve => setTimeout(resolve, 100));
            expect(field).toHaveValue('alice');
            await user.type(field, '2');
            await user.click(within(dialog).getByRole('button', { name: 'Save' }));

            expect(await within(dialog).findByText('This username is taken.')).toBeInTheDocument();
            expect(currentLocation()).toBe('/profile/alice/overview');
            // The open dialog hides the page from the accessibility tree
            expect(within(screen.getByRole('main', { hidden: true })).getByRole('heading', { name: 'alice', hidden: true })).toBeInTheDocument();
        });

        it('hides the follow button on the own profile', async () => {
            serveProfiles({ alice: mine() });
            renderApp('/profile/alice/overview', { user: alice });

            await ownerReady();
            expect(screen.queryByRole('button', { name: /^(Follow|Unfollow)$/ })).not.toBeInTheDocument();
        });

        knownIssue('W15', 'a failed bio save keeps the profile and the draft', async () => {
            serveProfiles({ alice: mine() });
            server.use(capture('patch', 'User/me', apiError(400, 'Bio is too long.')).handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();
            await user.click(screen.getAllByTestId('EditIcon')[1].closest('button')!);

            await user.click(screen.getByRole('button', { name: 'Save' }));

            expect(await screen.findByText('Bio is too long.')).toBeInTheDocument();
            expect(screen.getByPlaceholderText('Write something about yourself...')).toBeInTheDocument();
        });

        knownIssue('W16', 'saving the avatar does not reload the whole profile', async () => {
            const profiles = serveProfiles({ alice: mine() });
            server.use(capture('patch', 'User/me', ok({ message: 'Profile updated.' })).handler);
            const { user } = renderApp('/profile/alice/overview', { user: alice });
            await ownerReady();
            await waitFor(() => expect(profiles.calls.length).toBeGreaterThan(0));
            const before = profiles.calls.length;

            await user.click(screen.getAllByTestId('EditIcon')[0].closest('button')!);
            const dialog = await screen.findByRole('dialog', { name: 'Update Profile Picture' });
            await user.type(within(dialog).getByLabelText('Image URL'), 'https://img.example/new.png');
            await user.click(within(dialog).getByRole('button', { name: 'Save' }));
            await new Promise(resolve => setTimeout(resolve, 200));

            // The profile effect depends on the logged-in user object, so setUser refetches it (and remounts the tabs)
            expect(profiles.calls.length).toBe(before);
        });
    });
});
