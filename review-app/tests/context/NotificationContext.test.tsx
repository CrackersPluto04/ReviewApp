import { describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/preact';
import { AuthProvider, useAuth } from '../../src/context/AuthContext';
import { NotificationProvider, useNotifications } from '../../src/context/NotificationContext';
import { apiFetch } from '../../src/services/apiClient';
import { server } from '../support/server';
import { apiError, apiUrl, capture, deferred, ok } from '../support/api';
import { authUser } from '../support/builders';
import { loginAs, setupUser, useFakeTimers } from '../support/render';
import { muteConsoleError, setVisibility } from '../support/browser';

function CountProbe() {
    const { unseenCount, adjustUnseenCount, setUnseenCount } = useNotifications();
    const { logout } = useAuth();
    return <div>
        <span data-testid="count">{unseenCount}</span>
        <button onClick={() => adjustUnseenCount(-1)}>minus</button>
        <button onClick={() => adjustUnseenCount(2)}>plus</button>
        <button onClick={() => setUnseenCount(-5)}>negative</button>
        <button onClick={() => logout()}>logout</button>
    </div>;
}

const count = () => screen.getByTestId('count').textContent;

function serveCount(initial: number) {
    let value = initial;
    const endpoint = capture('get', 'Notification/unseen-count', () => ok({ count: value }));
    server.use(endpoint.handler);
    return { endpoint, set: (next: number) => { value = next; } };
}

function renderNotifications() {
    return render(<AuthProvider><NotificationProvider><CountProbe /></NotificationProvider></AuthProvider>);
}

describe('NotificationContext', () => {
    it('is 0 and never asks the API while logged out', async () => {
        loginAs(null);
        const unseen = serveCount(3);
        renderNotifications();

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(count()).toBe('0');
        expect(unseen.endpoint.calls).toHaveLength(0);
    });

    it('loads the count on login', async () => {
        loginAs(authUser());
        serveCount(3);
        renderNotifications();

        await waitFor(() => expect(count()).toBe('3'));
    });

    it('polls every 45 seconds while the tab is visible', async () => {
        useFakeTimers();
        loginAs(authUser());
        const unseen = serveCount(1);
        renderNotifications();
        await waitFor(() => expect(count()).toBe('1'));

        unseen.set(4);
        await act(() => vi.advanceTimersByTimeAsync(44_000));
        expect(count()).toBe('1');

        await act(() => vi.advanceTimersByTimeAsync(1_000));
        await waitFor(() => expect(count()).toBe('4'));
        expect(unseen.endpoint.calls).toHaveLength(2);
    });

    it('skips polling while the tab is hidden and refreshes when it is shown again', async () => {
        useFakeTimers();
        loginAs(authUser());
        const unseen = serveCount(1);
        renderNotifications();
        await waitFor(() => expect(count()).toBe('1'));

        act(() => setVisibility('hidden'));
        await act(() => vi.advanceTimersByTimeAsync(3 * 45_000));
        expect(unseen.endpoint.calls).toHaveLength(1);

        unseen.set(7);
        act(() => setVisibility('visible'));
        await waitFor(() => expect(count()).toBe('7'));
        expect(unseen.endpoint.calls).toHaveLength(2);
    });

    it('refreshes 800 ms after the user\'s own changes, once for several in a row', async () => {
        useFakeTimers();
        loginAs(authUser());
        const unseen = serveCount(0);
        server.use(capture('post', 'Review', ok({})).handler);
        renderNotifications();
        await waitFor(() => expect(unseen.endpoint.calls).toHaveLength(1));

        unseen.set(2);
        await apiFetch(apiUrl('Review'), { method: 'POST' });
        await act(() => vi.advanceTimersByTimeAsync(500));
        await apiFetch(apiUrl('Review'), { method: 'POST' });
        await act(() => vi.advanceTimersByTimeAsync(799));
        expect(unseen.endpoint.calls).toHaveLength(1);

        await act(() => vi.advanceTimersByTimeAsync(1));
        await waitFor(() => expect(count()).toBe('2'));
        expect(unseen.endpoint.calls).toHaveLength(2);
    });

    it('does not refresh after reads or failed changes', async () => {
        useFakeTimers();
        loginAs(authUser());
        const unseen = serveCount(0);
        server.use(
            capture('get', 'Review/1', ok({})).handler,
            capture('post', 'Review', apiError(400, 'Bad')).handler,
        );
        renderNotifications();
        await waitFor(() => expect(unseen.endpoint.calls).toHaveLength(1));

        await apiFetch(apiUrl('Review/1'));
        await apiFetch(apiUrl('Review'), { method: 'POST' });
        await act(() => vi.advanceTimersByTimeAsync(2_000));

        expect(unseen.endpoint.calls).toHaveLength(1);
    });

    it('resets to 0 on logout and stops polling', async () => {
        useFakeTimers();
        loginAs(authUser());
        const unseen = serveCount(5);
        server.use(capture('post', 'Auth/logout', ok({})).handler);
        renderNotifications();
        await waitFor(() => expect(count()).toBe('5'));

        await setupUser().click(screen.getByRole('button', { name: 'logout' }));
        await waitFor(() => expect(count()).toBe('0'));

        await act(() => vi.advanceTimersByTimeAsync(2 * 45_000));
        expect(unseen.endpoint.calls).toHaveLength(1);
    });

    it('drops a count that arrives after logging out', async () => {
        loginAs(authUser());
        const response = deferred(() => ok({ count: 9 }));
        server.use(
            capture('get', 'Notification/unseen-count', () => response.promise).handler,
            capture('post', 'Auth/logout', ok({})).handler,
        );
        renderNotifications();
        await waitFor(() => expect(screen.getByRole('button', { name: 'logout' })).toBeInTheDocument());

        await setupUser().click(screen.getByRole('button', { name: 'logout' }));
        response.release();

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(count()).toBe('0');
    });

    it('keeps the last count when a refresh fails', async () => {
        muteConsoleError();
        useFakeTimers();
        loginAs(authUser());
        let fail = false;
        server.use(capture('get', 'Notification/unseen-count', () => fail ? apiError(500, 'Down') : ok({ count: 3 })).handler);
        renderNotifications();
        await waitFor(() => expect(count()).toBe('3'));

        fail = true;
        await act(() => vi.advanceTimersByTimeAsync(45_000));

        expect(count()).toBe('3');
    });

    it('adjusts the count locally, never below 0', async () => {
        loginAs(authUser());
        serveCount(1);
        renderNotifications();
        await waitFor(() => expect(count()).toBe('1'));
        const user = setupUser();

        await user.click(screen.getByRole('button', { name: 'plus' }));
        expect(count()).toBe('3');

        await user.click(screen.getByRole('button', { name: 'minus' }));
        await user.click(screen.getByRole('button', { name: 'minus' }));
        await user.click(screen.getByRole('button', { name: 'minus' }));
        await user.click(screen.getByRole('button', { name: 'minus' }));
        expect(count()).toBe('0');

        await user.click(screen.getByRole('button', { name: 'plus' }));
        await user.click(screen.getByRole('button', { name: 'negative' }));
        expect(count()).toBe('0');
    });

    it('throws when used outside the provider', () => {
        muteConsoleError();
        expect(() => render(<AuthProvider><CountProbe /></AuthProvider>)).toThrow('useNotifications must be used within a NotificationProvider');
    });
});
