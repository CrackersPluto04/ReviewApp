import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { EditAvatarDialog } from '../../src/components/EditAvatarDialog';
import { setupUser } from '../support/render';
import { knownIssue } from '../support/known-issue';

describe('EditAvatarDialog', () => {
    it('previews and saves the pasted URL', async () => {
        const onSave = vi.fn();
        const user = setupUser();
        render(<EditAvatarDialog open currentAvatarUrl="" onClose={vi.fn()} onSave={onSave} />);

        expect(screen.queryByRole('img')).not.toBeInTheDocument();
        await user.type(screen.getByLabelText('Image URL'), 'https://img.example/me.png');

        expect(screen.getByRole('img')).toHaveAttribute('src', 'https://img.example/me.png');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        expect(onSave).toHaveBeenCalledWith('https://img.example/me.png');
    });

    it('starts from the current picture and can be cancelled', async () => {
        const onClose = vi.fn();
        const user = setupUser();
        render(<EditAvatarDialog open currentAvatarUrl="https://img.example/old.png" onClose={onClose} onSave={vi.fn()} />);

        expect(screen.getByLabelText('Image URL')).toHaveValue('https://img.example/old.png');
        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(onClose).toHaveBeenCalled();
    });

    knownIssue('W17', 'a cancelled draft is gone when the dialog is opened again', async () => {
        const user = setupUser();
        const props = { currentAvatarUrl: 'https://img.example/old.png', onClose: vi.fn(), onSave: vi.fn() };
        const { rerender } = render(<EditAvatarDialog open {...props} />);
        await user.clear(screen.getByLabelText('Image URL'));
        await user.type(screen.getByLabelText('Image URL'), 'https://typo');

        rerender(<EditAvatarDialog open={false} {...props} />);
        rerender(<EditAvatarDialog open {...props} />);

        await waitFor(() => expect(screen.getByLabelText('Image URL')).toHaveValue('https://img.example/old.png'));
    });
});
