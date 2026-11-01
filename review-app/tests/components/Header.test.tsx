import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { Header } from '../../src/components/Header';
import { server } from '../support/server';
import { capture, deferred, ok } from '../support/api';
import { authUser, premiumAuthUser } from '../support/builders';
import { currentLocation, currentLocationState, renderWithApp } from '../support/render';
import { stubLocation } from '../support/browser';
import { HttpResponse } from 'msw';

function renderHeader(options: Parameters<typeof renderWithApp>[1] = {}, mode: 'light' | 'dark' = 'light') {
    const toggleTheme = vi.fn();
    const result = renderWithApp(<Header mode={mode} toggleTheme={toggleTheme} />, options);
    return { ...result, toggleTheme };
}

describe('Header', () => {
    it('navigates with the main links', async () => {
        const { user } = renderHeader({ user: null });

        await user.click(screen.getByRole('button', { name: 'Movies' }));
        expect(currentLocation()).toBe('/discover?type=movie');

        await user.click(screen.getByRole('button', { name: 'Series' }));
        expect(currentLocation()).toBe('/discover?type=series');

        await user.click(screen.getByRole('button', { name: 'Music' }));
        expect(currentLocation()).toBe('/discover/music');

        await user.click(screen.getByRole('button', { name: 'Premium' }));
        expect(currentLocation()).toBe('/premium');

        await user.click(screen.getByText('THE Review App'));
        expect(currentLocation()).toBe('/');
    });

    it('shows a spinner while the session is being checked', async () => {
        const response = deferred(() => HttpResponse.json({}, { status: 401 }));
        server.use(capture('get', 'Auth/check-auth', () => response.promise).handler);
        renderHeader();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Login' })).not.toBeInTheDocument();

        response.release();
        expect(await screen.findByRole('button', { name: 'Login' })).toBeInTheDocument();
    });

    it('sends a visitor to login, remembering where they were', async () => {
        const { user } = renderHeader({ user: null, route: '/media/movie/42?tab=x' });

        await user.click(await screen.findByRole('button', { name: 'Login' }));

        expect(currentLocation()).toBe('/login');
        expect(currentLocationState().returnTo).toMatchObject({ pathname: '/media/movie/42', search: '?tab=x' });
        expect(screen.queryByRole('button', { name: /notifications/i })).not.toBeInTheDocument();
    });

    it('toggles the theme', async () => {
        const { user, toggleTheme } = renderHeader({ user: null }, 'dark');
        await screen.findByRole('button', { name: 'Login' });

        // The theme button is the only unlabelled icon button
        await user.click(screen.getByTestId('Brightness7Icon').closest('button')!);

        expect(toggleTheme).toHaveBeenCalledTimes(1);
    });

    it('shows the light / dark icon of the current mode', async () => {
        renderHeader({ user: null }, 'light');
        expect(await screen.findByTestId('Brightness4Icon')).toBeInTheDocument();
    });

    describe('logged in', () => {
        it('shows the bell and the account menu instead of Login', async () => {
            renderHeader({ user: authUser() });

            expect(await screen.findByRole('button', { name: 'Account menu' })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Login' })).not.toBeInTheDocument();
        });

        it('opens the profile tabs from the menu', async () => {
            const { user } = renderHeader({ user: authUser({ username: 'a b' }) });

            for (const [item, tab] of [['My Profile', 'overview'], ['Reviews', 'reviews'], ['Collections', 'collections'], ['Achievements', 'achievements']]) {
                await user.click(await screen.findByRole('button', { name: 'Account menu' }));
                await user.click(screen.getByRole('menuitem', { name: item }));
                expect(currentLocation()).toBe(`/profile/a%20b/${tab}`);
                await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
            }
        });

        it('offers Premium to a Free user', async () => {
            const { user } = renderHeader({ user: authUser() });
            await user.click(await screen.findByRole('button', { name: 'Account menu' }));

            await user.click(screen.getByRole('menuitem', { name: 'Get Premium' }));

            expect(currentLocation()).toBe('/premium');
        });

        it('marks a subscriber and offers to manage the subscription', async () => {
            const { user } = renderHeader({ user: premiumAuthUser() });

            await user.click(await screen.findByRole('button', { name: 'Account menu (Premium)' }));

            expect(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Manage subscription' })).toBeInTheDocument();
        });

        it('logs out and reloads the app from the start', async () => {
            const location = stubLocation('https://localhost:5173/profile/alice/overview');
            const logout = capture('post', 'Auth/logout', ok({}));
            server.use(logout.handler);
            const { user } = renderHeader({ user: authUser() });
            await user.click(await screen.findByRole('button', { name: 'Account menu' }));

            await user.click(screen.getByRole('menuitem', { name: 'Logout' }));

            await waitFor(() => expect(location.href).toBe('/'));
            expect(logout.calls).toHaveLength(1);
        });
    });
});
