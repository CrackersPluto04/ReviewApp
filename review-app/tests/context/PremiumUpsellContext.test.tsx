import { describe, expect, it } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/preact';
import { MemoryRouter } from 'react-router-dom';
import { usePremiumUpsell } from '../../src/context/PremiumUpsellContext';
import { apiFetch } from '../../src/services/apiClient';
import { server } from '../support/server';
import { apiUrl, capture, ok, paymentRequired } from '../support/api';
import { premiumAuthUser } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { muteConsoleError } from '../support/browser';

function UpsellButton() {
    const { showUpsell } = usePremiumUpsell();
    return <button onClick={showUpsell}>Unlock stats</button>;
}

const dialog = () => screen.queryByRole('dialog', { name: 'This is a Premium feature' });

describe('PremiumUpsellContext', () => {
    it('opens the dialog on a 402 and reloads the user', async () => {
        let checks = 0;
        server.use(
            capture('get', 'Auth/check-auth', () => { checks++; return ok(premiumAuthUser()); }).handler,
            capture('get', 'Subscription/entitlement-preview', paymentRequired()).handler,
        );
        renderWithApp(<p>page</p>);
        await waitFor(() => expect(checks).toBe(1));

        await act(async () => void await apiFetch(apiUrl('Subscription/entitlement-preview')));

        expect(await screen.findByRole('dialog', { name: 'This is a Premium feature' })).toBeInTheDocument();
        await waitFor(() => expect(checks).toBe(2));
    });

    it('opens on request (inline locked features)', async () => {
        const { user } = renderWithApp(<UpsellButton />);

        await user.click(screen.getByRole('button', { name: 'Unlock stats' }));

        expect(await screen.findByRole('dialog', { name: 'This is a Premium feature' })).toHaveTextContent('You can cancel any time.');
    });

    it('"See Premium" goes to the plans page', async () => {
        const { user } = renderWithApp(<UpsellButton />, { route: '/media/movie/1' });
        await user.click(screen.getByRole('button', { name: 'Unlock stats' }));

        await user.click(await screen.findByRole('button', { name: 'See Premium' }));

        await waitFor(() => expect(dialog()).not.toBeInTheDocument());
        expect(currentLocation()).toBe('/premium');
    });

    it('"Not now" closes it and stays on the page', async () => {
        const { user } = renderWithApp(<UpsellButton />, { route: '/media/movie/1' });
        await user.click(screen.getByRole('button', { name: 'Unlock stats' }));

        await user.click(await screen.findByRole('button', { name: 'Not now' }));

        await waitFor(() => expect(dialog()).not.toBeInTheDocument());
        expect(currentLocation()).toBe('/media/movie/1');
    });

    it('throws when used outside the provider', () => {
        muteConsoleError();
        expect(() => render(<MemoryRouter><UpsellButton /></MemoryRouter>)).toThrow('usePremiumUpsell must be used within a PremiumUpsellProvider');
    });
});
