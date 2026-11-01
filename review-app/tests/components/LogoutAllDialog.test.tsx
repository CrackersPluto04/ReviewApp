import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { LogoutAllDialog } from '../../src/components/LogoutAllDialog';
import { setupUser } from '../support/render';
import { fail, pending, succeed } from '../support/actions';

describe('LogoutAllDialog', () => {
    it('confirms and keeps the button disabled while it runs', async () => {
        const call = pending();
        const user = setupUser();
        render(<LogoutAllDialog open onClose={vi.fn()} onConfirm={call.fn} />);

        expect(screen.getByText(/including this one, will be logged out/)).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Log out everywhere' }));

        expect(screen.getByRole('button', { name: 'Log out everywhere' })).toBeDisabled();
        expect(call.fn).toHaveBeenCalledTimes(1);
        call.resolve({ success: true });
    });

    it('shows the error and allows a retry', async () => {
        const user = setupUser();
        render(<LogoutAllDialog open onClose={vi.fn()} onConfirm={fail('Network error.')} />);

        await user.click(screen.getByRole('button', { name: 'Log out everywhere' }));

        expect(await screen.findByText('Network error.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Log out everywhere' })).toBeEnabled();
    });

    it('has a fallback error and a cancel button', async () => {
        const onClose = vi.fn();
        const user = setupUser();
        render(<LogoutAllDialog open onClose={onClose} onConfirm={fail()} />);

        await user.click(screen.getByRole('button', { name: 'Log out everywhere' }));
        expect(await screen.findByText('Something went wrong. Please try again.')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(onClose).toHaveBeenCalled();
    });
});
