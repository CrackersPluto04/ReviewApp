import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { CancelSubscriptionDialog } from '../../src/components/CancelSubscriptionDialog';
import { setupUser } from '../support/render';
import { fail, pending, succeed } from '../support/actions';

describe('CancelSubscriptionDialog', () => {
    it('explains until when Premium stays', () => {
        render(<CancelSubscriptionDialog open accessEndsOn="December 1, 2026" onClose={vi.fn()} onConfirm={succeed()} />);

        expect(screen.getByRole('dialog', { name: 'Cancel Premium?' })).toHaveTextContent(
            "Your subscription won't renew. You keep every Premium feature until December 1, 2026, and you can resume it until then.");
    });

    it('falls back to the paid period without a date', () => {
        render(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={succeed()} />);

        expect(screen.getByRole('dialog')).toHaveTextContent('until the end of the period you paid for');
    });

    it('confirms once, with the button disabled while it runs', async () => {
        const call = pending();
        const user = setupUser();
        render(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={call.fn} />);

        await user.click(screen.getByRole('button', { name: 'Cancel subscription' }));

        expect(screen.getByRole('button', { name: 'Cancel subscription' })).toBeDisabled();
        expect(call.fn).toHaveBeenCalledTimes(1);
        call.resolve({ success: true });
    });

    it('shows the error, with a fallback, and allows a retry', async () => {
        const user = setupUser();
        const { rerender } = render(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={fail('The provider is down.')} />);

        await user.click(screen.getByRole('button', { name: 'Cancel subscription' }));
        expect(await screen.findByText('The provider is down.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Cancel subscription' })).toBeEnabled();

        rerender(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={fail()} />);
        await user.click(screen.getByRole('button', { name: 'Cancel subscription' }));
        expect(await screen.findByText('Something went wrong. Please try again.')).toBeInTheDocument();
    });

    it('clears the error when opened again', async () => {
        const user = setupUser();
        const onConfirm = fail('The provider is down.');
        const { rerender } = render(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={onConfirm} />);
        await user.click(screen.getByRole('button', { name: 'Cancel subscription' }));
        await screen.findByText('The provider is down.');

        rerender(<CancelSubscriptionDialog open={false} accessEndsOn="" onClose={vi.fn()} onConfirm={onConfirm} />);
        rerender(<CancelSubscriptionDialog open accessEndsOn="" onClose={vi.fn()} onConfirm={onConfirm} />);

        await waitFor(() => expect(screen.queryByText('The provider is down.')).not.toBeInTheDocument());
    });

    it('"Keep Premium" closes', async () => {
        const onClose = vi.fn();
        const user = setupUser();
        render(<CancelSubscriptionDialog open accessEndsOn="" onClose={onClose} onConfirm={succeed()} />);

        await user.click(screen.getByRole('button', { name: 'Keep Premium' }));

        expect(onClose).toHaveBeenCalled();
    });
});
