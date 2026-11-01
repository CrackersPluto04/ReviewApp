import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { ChangeEmailDialog } from '../../src/components/ChangeEmailDialog';
import { setupUser } from '../support/render';
import { fail, succeed } from '../support/actions';

const save = () => screen.getByRole('button', { name: 'Save' });

describe('ChangeEmailDialog', () => {
    function renderDialog(onSave = succeed()) {
        const onClose = vi.fn();
        const user = setupUser();
        const result = render(<ChangeEmailDialog open onSave={onSave} onClose={onClose} />);
        return {
            ...result, user, onSave, onClose,
            email: screen.getByLabelText('New email'),
            confirm: screen.getByLabelText('Confirm new email'),
            password: screen.getByLabelText('Current password'),
        };
    }

    it('saves the new email with the password', async () => {
        const { user, email, confirm, password, onSave } = renderDialog();

        await user.type(email, ' new@example.com ');
        await user.type(confirm, 'NEW@example.com');
        await user.type(password, 'hunter22');
        await user.click(save());

        // The confirmation is compared case-insensitively
        expect(onSave).toHaveBeenCalledWith('new@example.com', 'hunter22');
    });

    it('saves with Enter in the password field', async () => {
        const { user, email, confirm, password, onSave } = renderDialog();

        await user.type(email, 'new@example.com');
        await user.type(confirm, 'new@example.com');
        await user.type(password, 'hunter22{Enter}');

        expect(onSave).toHaveBeenCalledTimes(1);
    });

    it('flags an invalid email and a mismatching confirmation', async () => {
        const { user, email, confirm, password } = renderDialog();

        await user.type(email, 'new@');
        expect(screen.getByText('Please enter a valid email address.')).toBeInTheDocument();
        await user.type(email, 'example.com');
        await user.type(confirm, 'other@example.com');
        expect(screen.getByText('Emails do not match.')).toBeInTheDocument();
        await user.type(password, 'x');

        expect(save()).toBeDisabled();
    });

    it('needs the password', async () => {
        const { user, email, confirm } = renderDialog();

        await user.type(email, 'new@example.com');
        await user.type(confirm, 'new@example.com');

        expect(save()).toBeDisabled();
    });

    it('caps the fields at the maximum email length', () => {
        const { email, confirm } = renderDialog();

        expect(email).toHaveAttribute('maxLength', '254');
        expect(confirm).toHaveAttribute('maxLength', '254');
    });

    it('clears the password and shows the error on failure', async () => {
        const { user, email, confirm, password } = renderDialog(fail('Wrong password.'));

        await user.type(email, 'new@example.com');
        await user.type(confirm, 'new@example.com');
        await user.type(password, 'wrong-pass{Enter}');

        expect(await screen.findByText('Wrong password.')).toBeInTheDocument();
        expect(password).toHaveValue('');
        expect(email).toHaveValue('new@example.com');
    });

    it('clears everything when closed and opened again', async () => {
        const { user, email, rerender, onSave, onClose } = renderDialog();
        await user.type(email, 'new@example.com');

        rerender(<ChangeEmailDialog open={false} onSave={onSave} onClose={onClose} />);
        rerender(<ChangeEmailDialog open onSave={onSave} onClose={onClose} />);

        await waitFor(() => expect(screen.getByLabelText('New email')).toHaveValue(''));
    });
});
