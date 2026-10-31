import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { SubscriptionReturnPage } from '../../src/pages/SubscriptionReturnPage';
import { server } from '../support/server';
import { apiError, capture, ok } from '../support/api';
import { activeSubscription, authUser, premiumAuthUser, subscriptionStatus } from '../support/builders';
import { currentLocation, renderWithApp, useFakeTimers } from '../support/render';

function serveSync(response: Response) {
    const endpoint = capture('post', 'Subscription/sync', response);
    server.use(endpoint.handler);
    return endpoint;
}

function serveStatus(isPremiumFromCall: number) {
    let calls = 0;
    const endpoint = capture('get', 'Subscription/status', () => {
        calls++;
        return HttpResponse.json(calls >= isPremiumFromCall ? activeSubscription() : subscriptionStatus());
    });
    server.use(endpoint.handler);
    return endpoint;
}

const renderReturn = (status = 'success', user: ReturnType<typeof authUser> | null = authUser()) =>
    renderWithApp(<SubscriptionReturnPage />, { route: `/subscription/return?status=${status}`, user });

describe('SubscriptionReturnPage', () => {
    it('confirms at once when the sync already sees Premium, and reloads the user', async () => {
        const sync = serveSync(ok({ ...activeSubscription(), message: 'ok' }));
        let checks = 0;
        server.use(capture('get', 'Auth/check-auth', () => { checks++; return ok(checks > 1 ? premiumAuthUser() : authUser()); }).handler);
        renderWithApp(<SubscriptionReturnPage />, { route: '/subscription/return?status=success' });

        expect(await screen.findByRole('status')).toHaveTextContent('Confirming your payment...');
        expect(await screen.findByRole('heading', { name: 'Welcome to Premium!' })).toBeInTheDocument();
        expect(screen.getByText(/Your Premium features are unlocked/)).toBeInTheDocument();
        expect(sync.calls).toHaveLength(1);
        await waitFor(() => expect(checks).toBe(2));
    });

    it('polls the status every 2 s until Premium shows up', async () => {
        useFakeTimers();
        serveSync(ok(subscriptionStatus()));
        const status = serveStatus(3);
        renderReturn();
        await screen.findByRole('status');
        await waitFor(() => expect(status.calls.length).toBe(0));

        await vi.advanceTimersByTimeAsync(2_100);
        await waitFor(() => expect(status.calls).toHaveLength(1));
        await vi.advanceTimersByTimeAsync(2_100);
        await vi.advanceTimersByTimeAsync(2_100);

        expect(await screen.findByRole('heading', { name: 'Welcome to Premium!' })).toBeInTheDocument();
        expect(status.calls).toHaveLength(3);
    });

    it('gives up after 30 s and says the payment is still processing', async () => {
        useFakeTimers();
        serveSync(ok(subscriptionStatus()));
        const status = serveStatus(Infinity);
        const { user } = renderReturn();
        await screen.findByRole('status');

        await vi.advanceTimersByTimeAsync(32_000);

        expect(await screen.findByText(/Your payment is still being processed/)).toBeInTheDocument();
        const calls = status.calls.length;
        expect(calls).toBeGreaterThanOrEqual(13);
        expect(calls).toBeLessThanOrEqual(16);
        await vi.advanceTimersByTimeAsync(10_000);
        expect(status.calls).toHaveLength(calls);

        await user.click(screen.getByRole('button', { name: 'View my subscription' }));
        expect(currentLocation()).toBe('/premium');
    });

    it('shows a sync error', async () => {
        serveSync(apiError(502, 'Could not reach the payment provider.'));
        renderReturn();

        expect(await screen.findByText('Could not reach the payment provider.')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Your subscription' })).toBeInTheDocument();
    });

    it('says nothing was charged after a cancelled checkout, without asking the API', async () => {
        const sync = serveSync(ok({}));
        const { user } = renderReturn('cancelled', null);

        expect(screen.getByRole('heading', { name: 'Checkout cancelled' })).toBeInTheDocument();
        expect(screen.getByText('Nothing was charged. You can subscribe whenever you like.')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Back to Premium' }));
        expect(currentLocation()).toBe('/premium');
        expect(sync.calls).toHaveLength(0);
    });

    it('treats a missing status as cancelled', () => {
        renderWithApp(<SubscriptionReturnPage />, { route: '/subscription/return' });
        expect(screen.getByRole('heading', { name: 'Checkout cancelled' })).toBeInTheDocument();
    });

    it('asks a logged-out user to log in', async () => {
        const sync = serveSync(ok({}));
        renderReturn('success', null);

        expect(await screen.findByText('Log in to see your subscription.')).toBeInTheDocument();
        expect(sync.calls).toHaveLength(0);
    });

    it('stops polling when the user leaves', async () => {
        useFakeTimers();
        serveSync(ok(subscriptionStatus()));
        const status = serveStatus(Infinity);
        const { unmount } = renderReturn();
        await screen.findByRole('status');
        await waitFor(() => expect(status.calls).toHaveLength(0));

        unmount();
        await vi.advanceTimersByTimeAsync(10_000);

        expect(status.calls).toHaveLength(0);
    });

    it('goes home from the result', async () => {
        serveSync(ok(activeSubscription()));
        const { user } = renderReturn();

        await user.click(await screen.findByRole('button', { name: 'Go to the home page' }));

        expect(currentLocation()).toBe('/home');
    });
});
