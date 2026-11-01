import { describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { AuthProvider, useAuth } from '../../src/context/AuthContext';
import { apiFetch } from '../../src/services/apiClient';
import { server } from '../support/server';
import { apiUrl, capture, deferred, ok, unauthorized } from '../support/api';
import { authUser } from '../support/builders';
import { loginAs, setupUser } from '../support/render';
import { muteConsoleError } from '../support/browser';

function AuthProbe() {
    const { user, isLoggedIn, isLoading, refreshUser, logout, setUser } = useAuth();
    return <div>
        <span data-testid="state">{isLoading ? 'loading' : isLoggedIn ? `in:${user!.username}` : 'out'}</span>
        <button onClick={() => refreshUser()}>refresh</button>
        <button onClick={() => logout()}>logout</button>
        <button onClick={() => setUser(authUser({ username: 'manual' }))}>set</button>
    </div>;
}

const state = () => screen.getByTestId('state').textContent;

function renderAuth() {
    return render(<AuthProvider><AuthProbe /></AuthProvider>);
}

describe('AuthContext', () => {
    it('is loading until check-auth answers, then logged in', async () => {
        const response = deferred(() => HttpResponse.json(authUser({ username: 'alice' })));
        server.use(capture('get', 'Auth/check-auth', () => response.promise).handler);
        renderAuth();

        expect(state()).toBe('loading');
        response.release();

        await waitFor(() => expect(state()).toBe('in:alice'));
    });

    it('is logged out when the cookie is missing or invalid', async () => {
        loginAs(null);
        renderAuth();

        await waitFor(() => expect(state()).toBe('out'));
    });

    it('is logged out when the backend can\'t be reached', async () => {
        muteConsoleError();
        server.use(capture('get', 'Auth/check-auth', HttpResponse.error()).handler);
        renderAuth();

        await waitFor(() => expect(state()).toBe('out'));
    });

    it('drops the user on any 401 (session revoked elsewhere)', async () => {
        loginAs(authUser({ username: 'alice' }));
        server.use(capture('get', 'Review/1', unauthorized()).handler);
        renderAuth();
        await waitFor(() => expect(state()).toBe('in:alice'));

        await apiFetch(apiUrl('Review/1'));

        await waitFor(() => expect(state()).toBe('out'));
    });

    it('stops listening for 401s after unmounting', async () => {
        loginAs(authUser());
        const { unmount } = renderAuth();
        await waitFor(() => expect(state()).toMatch(/^in:/));
        unmount();

        server.use(capture('get', 'Review/1', unauthorized()).handler);
        // Would throw on a state update of an unmounted provider if the handler were still set
        await expect(apiFetch(apiUrl('Review/1'))).resolves.toHaveProperty('status', 401);
    });

    it('reloads the user on refreshUser', async () => {
        let username = 'alice';
        server.use(capture('get', 'Auth/check-auth', () => ok(authUser({ username }))).handler);
        renderAuth();
        await waitFor(() => expect(state()).toBe('in:alice'));

        username = 'alice2';
        await setupUser().click(screen.getByRole('button', { name: 'refresh' }));

        await waitFor(() => expect(state()).toBe('in:alice2'));
    });

    it('logs out through the API', async () => {
        loginAs(authUser({ username: 'alice' }));
        const logout = capture('post', 'Auth/logout', ok({}));
        server.use(logout.handler);
        renderAuth();
        await waitFor(() => expect(state()).toBe('in:alice'));

        await setupUser().click(screen.getByRole('button', { name: 'logout' }));

        await waitFor(() => expect(state()).toBe('out'));
        expect(logout.calls).toHaveLength(1);
    });

    it('still logs out locally when the logout request fails', async () => {
        muteConsoleError();
        loginAs(authUser({ username: 'alice' }));
        server.use(capture('post', 'Auth/logout', HttpResponse.error()).handler);
        renderAuth();
        await waitFor(() => expect(state()).toBe('in:alice'));

        await setupUser().click(screen.getByRole('button', { name: 'logout' }));

        await waitFor(() => expect(state()).toBe('out'));
    });

    it('lets components set the user (after login)', async () => {
        loginAs(null);
        renderAuth();
        await waitFor(() => expect(state()).toBe('out'));

        await setupUser().click(screen.getByRole('button', { name: 'set' }));

        expect(state()).toBe('in:manual');
    });

    it('throws when used outside the provider', () => {
        muteConsoleError();
        expect(() => render(<AuthProbe />)).toThrow('useAuth must be used within an AuthProvider');
    });
});
