import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { CreateEditCollectionDialog } from '../../src/components/CreateEditCollectionDialog';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { collection } from '../support/builders';
import { setupUser } from '../support/render';

function renderDialog(props: Partial<Parameters<typeof CreateEditCollectionDialog>[0]> = {}) {
    const handlers = { onClose: vi.fn(), onSuccess: vi.fn() };
    const user = setupUser();
    const result = render(<CreateEditCollectionDialog open {...handlers} {...props} />);
    return { ...result, ...handlers, user };
}

const nameField = () => screen.getByLabelText('Collection Name');

describe('CreateEditCollectionDialog', () => {
    describe('create', () => {
        it('creates a private collection with the trimmed name', async () => {
            const create = capture('post', 'Collection', ok({ id: 9, name: 'Sci-fi' }));
            server.use(create.handler);
            const { user, onSuccess, onClose } = renderDialog();

            expect(screen.getByRole('dialog', { name: 'Create New Collection' })).toBeInTheDocument();
            expect(screen.getByRole('combobox')).toHaveTextContent('Private');
            await user.type(nameField(), '  Sci-fi ');
            await user.click(screen.getByRole('button', { name: 'Create' }));

            await waitFor(() => expect(onSuccess).toHaveBeenCalledWith(9));
            expect(onClose).toHaveBeenCalled();
            expect(create.last.body).toEqual({ name: 'Sci-fi', visibilityLevel: 0 });
        });

        it('creates with the chosen visibility', async () => {
            const create = capture('post', 'Collection', ok({ id: 9 }));
            server.use(create.handler);
            const { user } = renderDialog();

            await user.type(nameField(), 'Shared');
            await user.click(screen.getByRole('combobox'));
            await user.click(screen.getByRole('option', { name: 'Public' }));
            await user.click(screen.getByRole('button', { name: 'Create' }));

            await waitFor(() => expect(create.last.body).toEqual({ name: 'Shared', visibilityLevel: 1 }));
        });

        it('needs a name of at most 50 characters', async () => {
            const { user } = renderDialog();

            expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();
            await user.type(nameField(), '   ');
            expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();

            await user.clear(nameField());
            await user.click(nameField());
            await user.paste('x'.repeat(51));
            expect(screen.getByText('51 / 50')).toBeInTheDocument();
            expect(nameField()).toHaveAttribute('aria-invalid', 'true');
            expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();
        });

        it('shows the API\'s error and stays open', async () => {
            server.use(capture('post', 'Collection', apiError(409, 'You already have a collection with this name.')).handler);
            const { user, onSuccess, onClose } = renderDialog();

            await user.type(nameField(), 'Dupe');
            await user.click(screen.getByRole('button', { name: 'Create' }));

            expect(await screen.findByText('You already have a collection with this name.')).toBeInTheDocument();
            expect(onSuccess).not.toHaveBeenCalled();
            expect(onClose).not.toHaveBeenCalled();
        });

        it('locks the form while saving', async () => {
            const response = deferred(() => ok({ id: 9 }));
            server.use(capture('post', 'Collection', () => response.promise).handler);
            const { user } = renderDialog();

            await user.type(nameField(), 'Slow');
            await user.click(screen.getByRole('button', { name: 'Create' }));

            expect(await screen.findByRole('button', { name: 'Creating...' })).toBeDisabled();
            expect(nameField()).toBeDisabled();
            expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
            response.release();
        });

        it('cancels and starts empty when opened again', async () => {
            const { user, onClose, rerender } = renderDialog();
            await user.type(nameField(), 'Draft');

            await user.click(screen.getByRole('button', { name: 'Cancel' }));
            expect(onClose).toHaveBeenCalled();

            rerender(<CreateEditCollectionDialog open={false} onClose={onClose} onSuccess={vi.fn()} />);
            rerender(<CreateEditCollectionDialog open onClose={onClose} onSuccess={vi.fn()} />);
            await waitFor(() => expect(nameField()).toHaveValue(''));
        });
    });

    describe('edit', () => {
        const existing = collection({ id: 5, name: 'Sci-fi', visibilityLevel: 2 });

        it('starts from the collection and saves the changes', async () => {
            const updated = { ...existing, name: 'Space' };
            const update = capture('put', 'Collection', ok(updated));
            server.use(update.handler);
            const { user, onSuccess } = renderDialog({ collection: existing });

            expect(screen.getByRole('dialog', { name: 'Edit Collection' })).toBeInTheDocument();
            await waitFor(() => expect(nameField()).toHaveValue('Sci-fi'));
            expect(screen.getByRole('combobox')).toHaveTextContent('Followers Only');
            await user.clear(nameField());
            await user.type(nameField(), 'Space');
            await user.click(screen.getByRole('button', { name: 'Save Changes' }));

            await waitFor(() => expect(onSuccess).toHaveBeenCalledWith(updated));
            expect(update.last.body).toEqual({ collectionID: 5, name: 'Space', visibilityLevel: 2 });
        });

        it('shows Saving... while saving', async () => {
            const response = deferred(() => ok(existing));
            server.use(capture('put', 'Collection', () => response.promise).handler);
            const { user } = renderDialog({ collection: existing });
            await waitFor(() => expect(nameField()).toHaveValue('Sci-fi'));

            await user.click(screen.getByRole('button', { name: 'Save Changes' }));

            expect(await screen.findByRole('button', { name: 'Saving...' })).toBeDisabled();
            response.release();
        });
    });
});
