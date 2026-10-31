import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { SubscriptionPanel } from '../../src/components/SubscriptionPanel';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { activeSubscription, premiumAuthUser, subscriptionStatus } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubLocation } from '../support/browser';

function serveStatus(...statuses: ReturnType<typeof subscriptionStatus>[]) {
    let call = 0;
    const endpoint = capture('get', 'Subscription/status', () => HttpResponse.json(statuses[Math.min(call++, statuses.length - 1)]));
    server.use(endpoint.handler);
    return endpoint;
}

function renderPanel() {
    return renderWithApp(<SubscriptionPanel />, { user: premiumAuthUser() });
}

describe('SubscriptionPanel', () => {
    it('shows a spinner while loading', async () => {
        const response = deferred(() => ok(activeSubscription()));
        server.use(capture('get', 'Subscription/status', () => response.promise).handler);
        renderPanel();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
    });

    it('offers Premium on the Free plan', async () => {
        serveStatus(subscriptionStatus());
        const { user } = renderPanel();

        expect(await screen.findByText("You're on the Free plan")).toBeInTheDocument();
        expect(screen.getByText('Subscribe to Premium to unlock exclusive features.')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'See Premium' }));
        expect(currentLocation()).toBe('/premium');
    });

    it('invites back after the subscription ended', async () => {
        serveStatus(subscriptionStatus({ status: 'Ended' }));
        renderPanel();

        expect(await screen.findByText('Your Premium subscription has ended. You can subscribe again any time.')).toBeInTheDocument();
    });

    it('shows an active subscription with its renewal date', async () => {
        serveStatus(activeSubscription({ accessEndsAt: '2026-12-01T00:00:00Z' }));
        renderPanel();

        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
        expect(screen.getByLabelText('Premium member')).toBeInTheDocument();
        expect(screen.getByText('Next renewal:')).toHaveTextContent('Next renewal: December 1, 2026');
        expect(screen.getByText('You can cancel any time.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Cancel subscription' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Resume subscription' })).not.toBeInTheDocument();
    });

    it('shows a cancelled subscription that can be resumed', async () => {
        serveStatus(
            activeSubscription({ willRenew: false, cancelAtPeriodEnd: true, accessEndsAt: '2026-12-01T00:00:00Z' }),
            activeSubscription(),
        );
        const resume = capture('post', 'Subscription/resume', ok({ message: 'Your subscription renews again.' }));
        server.use(resume.handler);
        const { user } = renderPanel();

        expect(await screen.findByText('Cancelled')).toBeInTheDocument();
        expect(screen.getByText('Premium until:')).toHaveTextContent('Premium until: December 1, 2026');
        expect(screen.queryByRole('button', { name: 'Cancel subscription' })).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Resume subscription' }));

        expect(await screen.findByText('Your subscription renews again.')).toBeInTheDocument();
        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
        expect(resume.calls).toHaveLength(1);
    });

    it('warns about a failed payment with the grace period end', async () => {
        serveStatus(activeSubscription({ status: 'PastDue', willRenew: false, hasPaymentProblem: true, accessEndsAt: '2026-11-05T00:00:00Z' }));
        renderPanel();

        expect(await screen.findByText('Payment problem')).toBeInTheDocument();
        expect(screen.getByText(/We couldn't renew your subscription\. Update your payment details by November 5, 2026 to keep Premium\./)).toBeInTheDocument();
    });

    it('cancels after confirming in the dialog, then reloads the status and the user', async () => {
        serveStatus(activeSubscription({ accessEndsAt: '2026-12-01T00:00:00Z' }), activeSubscription({ willRenew: false, cancelAtPeriodEnd: true }));
        const cancel = capture('post', 'Subscription/cancel', ok({ message: 'Your subscription was cancelled.' }));
        let checks = 0;
        server.use(cancel.handler, capture('get', 'Auth/check-auth', () => { checks++; return ok(premiumAuthUser()); }).handler);
        // No user option: it would replace the counting check-auth handler
        const { user } = renderWithApp(<SubscriptionPanel />);

        await user.click(await screen.findByRole('button', { name: 'Cancel subscription' }));
        const dialog = await screen.findByRole('dialog', { name: 'Cancel Premium?' });
        expect(dialog).toHaveTextContent('You keep every Premium feature until December 1, 2026');
        await user.click(within(dialog).getByRole('button', { name: 'Cancel subscription' }));

        expect(await screen.findByText('Your subscription was cancelled.')).toBeInTheDocument();
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(await screen.findByText('Cancelled')).toBeInTheDocument();
        expect(cancel.calls).toHaveLength(1);
        await waitFor(() => expect(checks).toBeGreaterThanOrEqual(2));
    });

    it('keeps the dialog open with the error when cancelling fails', async () => {
        serveStatus(activeSubscription());
        server.use(capture('post', 'Subscription/cancel', apiError(502, 'The payment provider is not available.')).handler);
        const { user } = renderPanel();

        await user.click(await screen.findByRole('button', { name: 'Cancel subscription' }));
        const dialog = await screen.findByRole('dialog', { name: 'Cancel Premium?' });
        await user.click(within(dialog).getByRole('button', { name: 'Cancel subscription' }));

        expect(await within(dialog).findByText('The payment provider is not available.')).toBeInTheDocument();
        expect(within(dialog).getByRole('button', { name: 'Cancel subscription' })).toBeEnabled();
    });

    it('"Keep Premium" closes the dialog', async () => {
        serveStatus(activeSubscription({ accessEndsAt: null }));
        const { user } = renderPanel();

        await user.click(await screen.findByRole('button', { name: 'Cancel subscription' }));
        const dialog = await screen.findByRole('dialog', { name: 'Cancel Premium?' });
        expect(dialog).toHaveTextContent('until the end of the period you paid for');
        await user.click(within(dialog).getByRole('button', { name: 'Keep Premium' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('sends the browser to the payment portal', async () => {
        const location = stubLocation();
        serveStatus(activeSubscription());
        const portal = capture('post', 'Subscription/portal', ok({ url: 'https://pay.example/portal/123' }));
        server.use(portal.handler);
        const { user } = renderPanel();

        await user.click(await screen.findByRole('button', { name: 'Manage payment details' }));

        await waitFor(() => expect(location.href).toBe('https://pay.example/portal/123'));
        expect(portal.last.body).toEqual({ platform: 'web' });
        expect(screen.getByRole('button', { name: 'Manage payment details' })).toBeDisabled();
    });

    it('shows why the portal could not be opened', async () => {
        serveStatus(activeSubscription());
        server.use(capture('post', 'Subscription/portal', apiError(502, 'Could not open the payment settings.')).handler);
        const { user } = renderPanel();

        await user.click(await screen.findByRole('button', { name: 'Manage payment details' }));

        expect(await screen.findByText('Could not open the payment settings.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Manage payment details' })).toBeEnabled();
    });

    it('shows a status that could not be loaded', async () => {
        server.use(capture('get', 'Subscription/status', apiError(500, 'Could not load your subscription.')).handler);
        renderPanel();

        expect(await screen.findByText('Could not load your subscription.')).toBeInTheDocument();
    });
});
