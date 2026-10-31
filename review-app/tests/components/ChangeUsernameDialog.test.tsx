import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { ChangeUsernameDialog } from '../../src/components/ChangeUsernameDialog';
import { setupUser } from '../support/render';
import { fail, pending, succeed } from '../support/actions';

const save = () => screen.getByRole('button', { name: 'Save' });

describe('ChangeUsernameDialog', () => {
    function renderDialog(onSave = succeed(), currentUsername = 'alice') {
        const onClose = vi.fn();
        const user = setupUser();
        const result = render(<ChangeUsernameDialog open currentUsername={currentUsername} onSave={onSave} onClose={onClose} />);
        return { ...result, user, onSave, onClose, field: screen.getByLabelText('New username') };
    }

    it('starts from the current name, which can\'t be saved unchanged', async () => {
        const { field } = renderDialog();

        expect(screen.getByText('Current username')).toBeInTheDocument();
        await waitFor(() => expect(field).toHaveValue('alice'));
        expect(save()).toBeDisabled();
        expect(screen.getByText('5 / 20')).toBeInTheDocument();
    });

    it('saves the trimmed new name', async () => {
        const { user, field, onSave } = renderDialog();

        await user.clear(field);
        await user.type(field, '  alicia ');
        await user.click(save());

        expect(onSave).toHaveBeenCalledWith('alicia');
    });

    it('saves with Enter', async () => {
        const { user, field, onSave } = renderDialog();

        await user.clear(field);
        await user.type(field, 'alicia{Enter}');

        expect(onSave).toHaveBeenCalledWith('alicia');
    });

    it.each([
        ['ab', true],
        ['abc', false],
        ['a'.repeat(20), false],
        ['a'.repeat(21), true],
    ])('%s is %s invalid', async (name, invalid) => {
        const { user, field } = renderDialog();

        await user.clear(field);
        await user.type(field, name);

        expect(save()).toHaveProperty('disabled', invalid);
        expect(screen.queryByText('Must be between 3 and 20 characters.') !== null).toBe(invalid);
    });

    it('shows the save error and lets the user retry', async () => {
        const onSave = fail('This username is taken.');
        const { user, field } = renderDialog(onSave);

        await user.clear(field);
        await user.type(field, 'bob');
        await user.click(save());

        expect(await screen.findByText('This username is taken.')).toBeInTheDocument();
        expect(field).toHaveAttribute('aria-invalid', 'true');
        await user.type(field, 'by');
        expect(screen.queryByText('This username is taken.')).not.toBeInTheDocument();
        expect(save()).toBeEnabled();
    });

    it('has a fallback error message', async () => {
        const { user, field } = renderDialog(fail());

        await user.clear(field);
        await user.type(field, 'bobby');
        await user.click(save());

        expect(await screen.findByText('Failed to change username.')).toBeInTheDocument();
    });

    it('disables saving while it runs', async () => {
        const call = pending();
        const { user, field } = renderDialog(call.fn);

        await user.clear(field);
        await user.type(field, 'bobby');
        await user.click(save());

        expect(save()).toBeDisabled();
        await user.type(field, '{Enter}');
        expect(call.fn).toHaveBeenCalledTimes(1);
        call.resolve({ success: true });
    });

    it('cancels', async () => {
        const { user, onClose } = renderDialog();
        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(onClose).toHaveBeenCalled();
    });
});
