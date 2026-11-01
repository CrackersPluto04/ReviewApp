import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/preact';
import { SubscriptionDialog } from '../../src/components/SubscriptionDialog';
import { server } from '../support/server';
import { capture, ok } from '../support/api';
import { activeSubscription, premiumAuthUser } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubMatchMedia } from '../support/browser';

describe('SubscriptionDialog', () => {
    it('loads the subscription only while open', async () => {
        const status = capture('get', 'Subscription/status', ok(activeSubscription()));
        server.use(status.handler);
        const onClose = vi.fn();
        const { rerender } = renderWithApp(<SubscriptionDialog open={false} onClose={onClose} />, { user: premiumAuthUser() });

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(status.calls).toHaveLength(0);

        rerender(<SubscriptionDialog open onClose={onClose} />);
        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
        expect(status.calls).toHaveLength(1);
    });

    it('links to the plans page and closes', async () => {
        server.use(capture('get', 'Subscription/status', ok(activeSubscription())).handler);
        const onClose = vi.fn();
        const { user } = renderWithApp(<SubscriptionDialog open onClose={onClose} />, { user: premiumAuthUser() });

        await user.click(await screen.findByRole('button', { name: 'See plans' }));

        expect(onClose).toHaveBeenCalled();
        expect(currentLocation()).toBe('/premium');
    });

    it('is full screen on small screens', async () => {
        stubMatchMedia(query => query.includes('max-width'));
        server.use(capture('get', 'Subscription/status', ok(activeSubscription())).handler);
        renderWithApp(<SubscriptionDialog open onClose={vi.fn()} />, { user: premiumAuthUser() });

        expect(screen.getByRole('dialog').closest('.MuiDialog-paper') ?? screen.getByRole('dialog')).toHaveClass('MuiDialog-paperFullScreen');
        await screen.findByText('Renews automatically');
    });
});
