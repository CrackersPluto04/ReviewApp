import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { PremiumPage } from '../../src/pages/PremiumPage';
import { server } from '../support/server';
import { apiError, capture, deferred, ok, paymentRequired } from '../support/api';
import { activeSubscription, authUser, plan, premiumAuthUser } from '../support/builders';
import { currentLocation, currentLocationState, renderWithApp } from '../support/render';
import { stubLocation } from '../support/browser';

function servePlan(thePlan = plan()) {
    const endpoint = capture('get', 'Subscription/plan', ok(thePlan));
    server.use(endpoint.handler);
    return endpoint;
}

describe('PremiumPage', () => {
    it('compares Free and Premium for a visitor', async () => {
        servePlan(plan({ priceAmount: 4.99, currency: 'EUR', interval: 'month' }));
        renderWithApp(<PremiumPage />, { user: null, route: '/premium' });

        expect(screen.getByRole('heading', { name: 'THE Review App Premium' })).toBeInTheDocument();
        expect(await screen.findByText('€4.99 / month')).toBeInTheDocument();
        expect(screen.getByText('Everything the app offers today stays free')).toBeInTheDocument();
        expect(screen.getByText('Early access')).toBeInTheDocument();
        expect(screen.getByText('Coming soon')).toBeInTheDocument();
        expect(screen.getByText(/renews automatically every month/)).toBeInTheDocument();
        // Logged-out visitors get neither the preview nor a current plan
        expect(screen.queryByRole('button', { name: 'Open the early access preview' })).not.toBeInTheDocument();
        expect(screen.queryByText('Your current plan')).not.toBeInTheDocument();
    });

    it('waits for the plan, then shows its error', async () => {
        const response = deferred(() => apiError(500, 'Could not load the plans.'));
        server.use(capture('get', 'Subscription/plan', () => response.promise).handler);
        renderWithApp(<PremiumPage />, { user: null });

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('Could not load the plans.')).toBeInTheDocument();
    });

    it('sends a visitor to login before subscribing', async () => {
        servePlan();
        const { user } = renderWithApp(<PremiumPage />, { user: null, route: '/premium' });

        await user.click(await screen.findByRole('button', { name: 'Log in to subscribe' }));

        expect(currentLocation()).toBe('/login');
        expect(currentLocationState().returnTo.pathname).toBe('/premium');
    });

    it('sends a Free user to the checkout', async () => {
        const location = stubLocation();
        servePlan();
        const checkout = capture('post', 'Subscription/checkout', ok({ url: 'https://pay.example/checkout/1' }));
        server.use(checkout.handler);
        const { user } = renderWithApp(<PremiumPage />, { user: authUser() });

        expect(await screen.findByText('Your current plan')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Subscribe for €4.99 / month' }));

        await waitFor(() => expect(location.href).toBe('https://pay.example/checkout/1'));
        expect(checkout.last.body).toEqual({ platform: 'web' });
        expect(screen.getByRole('button', { name: /Subscribe for/ })).toBeDisabled();
    });

    it('shows why the checkout could not start', async () => {
        servePlan();
        server.use(capture('post', 'Subscription/checkout', apiError(409, 'You already have Premium.')).handler);
        const { user } = renderWithApp(<PremiumPage />, { user: authUser() });

        await user.click(await screen.findByRole('button', { name: /Subscribe for/ }));

        expect(await screen.findByText('You already have Premium.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Subscribe for/ })).toBeEnabled();
    });

    it('shows a subscriber their subscription instead of the subscribe button', async () => {
        servePlan();
        server.use(capture('get', 'Subscription/status', ok(activeSubscription())).handler);
        renderWithApp(<PremiumPage />, { user: premiumAuthUser() });

        expect(await screen.findByRole('heading', { name: 'Your subscription' })).toBeInTheDocument();
        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
        expect(screen.getByText('Your current plan')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Subscribe for/ })).not.toBeInTheDocument();
    });

    it('opens the early access preview for a subscriber', async () => {
        servePlan();
        server.use(
            capture('get', 'Subscription/status', ok(activeSubscription())).handler,
            capture('get', 'Subscription/entitlement-preview', ok({ message: 'Welcome to the preview!' })).handler,
        );
        const { user } = renderWithApp(<PremiumPage />, { user: premiumAuthUser() });

        await user.click(await screen.findByRole('button', { name: 'Open the early access preview' }));

        expect(await screen.findByText('Welcome to the preview!')).toBeInTheDocument();
    });

    it('leaves a Free user\'s preview attempt to the upsell dialog', async () => {
        servePlan();
        server.use(capture('get', 'Subscription/entitlement-preview', paymentRequired()).handler);
        const { user } = renderWithApp(<PremiumPage />, { user: authUser() });

        await user.click(await screen.findByRole('button', { name: 'Open the early access preview' }));

        expect(await screen.findByRole('dialog', { name: 'This is a Premium feature' })).toBeInTheDocument();
        expect(screen.queryByText('This is a Premium feature.')).not.toBeInTheDocument();
    });

    it('shows other preview errors on the page', async () => {
        servePlan();
        server.use(capture('get', 'Subscription/entitlement-preview', apiError(500, 'The preview is broken.')).handler);
        const { user } = renderWithApp(<PremiumPage />, { user: authUser() });

        await user.click(await screen.findByRole('button', { name: 'Open the early access preview' }));

        expect(await screen.findByText('The preview is broken.')).toBeInTheDocument();
    });
});
