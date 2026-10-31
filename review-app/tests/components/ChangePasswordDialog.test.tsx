import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { ChangePasswordDialog } from '../../src/components/ChangePasswordDialog';
import { setupUser } from '../support/render';
import { fail, succeed } from '../support/actions';

const save = () => screen.getByRole('button', { name: 'Save' });

describe('ChangePasswordDialog', () => {
    function renderDialog(onSave = succeed()) {
        const onClose = vi.fn();
        const user = setupUser();
        const result = render(<ChangePasswordDialog open onSave={onSave} onClose={onClose} />);
        return {
            ...result, user, onSave, onClose,
            current: screen.getByLabelText('Current password'),
            next: screen.getByLabelText('New password'),
            confirm: screen.getByLabelText('Confirm new password'),
        };
    }

    it('saves a valid new password', async () => {
        const { user, current, next, confirm, onSave } = renderDialog();

        await user.type(current, 'old-pass1');
        await user.type(next, 'new-pass1');
        await user.type(confirm, 'new-pass1');
        await user.click(save());

        expect(onSave).toHaveBeenCalledWith('old-pass1', 'new-pass1');
        expect(screen.getByText(/You will stay logged in on this device/)).toBeInTheDocument();
    });

    it('checks the length, that it changed and the confirmation', async () => {
        const { user, current, next, confirm } = renderDialog();
        await user.type(current, 'old-pass1');

        await user.type(next, 'short');
        expect(screen.getByText('Must be between 8 and 20 characters.')).toBeInTheDocument();

        await user.clear(next);
        await user.type(next, 'old-pass1');
        expect(screen.getByText('Must be different from the current password.')).toBeInTheDocument();

        await user.clear(next);
        await user.type(next, 'new-pass1');
        await user.type(confirm, 'new-pass2');
        expect(screen.getByText('Passwords do not match.')).toBeInTheDocument();
        expect(save()).toBeDisabled();

        await user.clear(confirm);
        await user.type(confirm, 'new-pass1');
        expect(save()).toBeEnabled();
    });

    it('caps the new password at 20 characters', () => {
        const { next, confirm } = renderDialog();

        expect(next).toHaveAttribute('maxLength', '20');
        expect(confirm).toHaveAttribute('maxLength', '20');
    });

    it('clears the current password and shows the error on failure', async () => {
        const { user, current, next, confirm } = renderDialog(fail('The current password is wrong.'));

        await user.type(current, 'old-pass1');
        await user.type(next, 'new-pass1');
        await user.type(confirm, 'new-pass1{Enter}');

        expect(await screen.findByText('The current password is wrong.')).toBeInTheDocument();
        expect(current).toHaveValue('');
        expect(next).toHaveValue('new-pass1');
    });

    it('has a fallback error message', async () => {
        const { user, current, next, confirm } = renderDialog(fail());

        await user.type(current, 'old-pass1');
        await user.type(next, 'new-pass1');
        await user.type(confirm, 'new-pass1{Enter}');

        expect(await screen.findByText('Failed to change password.')).toBeInTheDocument();
    });
});
