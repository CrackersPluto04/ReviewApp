import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { CollectionDetails } from '../../src/components/CollectionDetails';
import { server } from '../support/server';
import { apiError, capture, deferred, noContent, ok } from '../support/api';
import { FOLLOWERS_ONLY, PRIVATE, authUser, collection, collectionMedia, favourites, movie } from '../support/builders';
import { renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';
import { stubConfirm } from '../support/browser';

function serveDetails(details: { collection: ReturnType<typeof collection>; mediaItems: ReturnType<typeof collectionMedia>[] } | Response) {
    const endpoint = capture('get', 'Collection/:id', details instanceof Response ? details : HttpResponse.json(details));
    server.use(endpoint.handler);
    return endpoint;
}

function renderDetails(collectionId = 5) {
    const handlers = { onCollectionEdited: vi.fn(), onCollectionDeleted: vi.fn(), onMediaRemoved: vi.fn() };
    const result = renderWithApp(<CollectionDetails collectionId={collectionId} {...handlers} />, { user: authUser() });
    return { ...result, ...handlers };
}

const mine = (overrides: Record<string, any> = {}) => collection({ id: 5, name: 'Sci-fi', isOwner: true, createdAt: '2026-02-01', ...overrides });

describe('CollectionDetails', () => {
    it('shows the collection and its media', async () => {
        serveStats();
        const details = serveDetails({ collection: mine({ visibilityLevel: FOLLOWERS_ONLY }), mediaItems: [
            collectionMedia({ media: movie({ title: 'Arrival' }) }), collectionMedia({ media: movie({ title: 'Contact' }) }),
        ] });
        renderDetails();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        expect(await screen.findByRole('heading', { name: 'Sci-fi' })).toBeInTheDocument();
        expect(screen.getByText(/Followers Only • Created: 2026-02-01 • 2 items/)).toBeInTheDocument();
        expect(screen.getByText('Arrival')).toBeInTheDocument();
        expect(screen.getByText('Contact')).toBeInTheDocument();
        expect(details.last.url.pathname).toBe('/api/Collection/5');
    });

    it('invites the owner to fill an empty collection', async () => {
        serveDetails({ collection: mine({ visibilityLevel: PRIVATE }), mediaItems: [] });
        renderDetails();

        expect(await screen.findByText('This collection is empty.')).toBeInTheDocument();
        expect(screen.getByText('Search for movies or music to add them here!')).toBeInTheDocument();
        expect(screen.getByText(/Private • Created/)).toBeInTheDocument();
    });

    it('shows a visitor no owner actions', async () => {
        serveDetails({ collection: mine({ isOwner: false }), mediaItems: [] });
        renderDetails();

        await screen.findByText('This collection is empty.');
        expect(screen.queryByText('Search for movies or music to add them here!')).not.toBeInTheDocument();
        expect(screen.queryByTestId('EditIcon')).not.toBeInTheDocument();
        expect(screen.queryByTestId('DeleteIcon')).not.toBeInTheDocument();
    });

    it('never offers to edit or delete Favourites', async () => {
        serveDetails({ collection: favourites({ id: 5, isOwner: true }), mediaItems: [] });
        renderDetails();

        await screen.findByRole('heading', { name: 'Favourites' });
        expect(screen.queryByTestId('EditIcon')).not.toBeInTheDocument();
    });

    it('shows the loading error', async () => {
        serveDetails(HttpResponse.json({ error: 'This collection is private.' }, { status: 403 }));
        renderDetails();

        expect(await screen.findByText('This collection is private.')).toBeInTheDocument();
    });

    it('reloads when another collection is chosen', async () => {
        const details = capture('get', 'Collection/:id', (_req, params) => ok({ collection: mine({ id: Number(params.id), name: `Collection ${params.id}` }), mediaItems: [] }));
        server.use(details.handler);
        const handlers = { onCollectionEdited: vi.fn(), onCollectionDeleted: vi.fn(), onMediaRemoved: vi.fn() };
        const { rerender } = renderWithApp(<CollectionDetails collectionId={5} {...handlers} />);
        await screen.findByRole('heading', { name: 'Collection 5' });

        rerender(<CollectionDetails collectionId={6} {...handlers} />);

        expect(await screen.findByRole('heading', { name: 'Collection 6' })).toBeInTheDocument();
    });

    it('edits the name and visibility', async () => {
        serveDetails({ collection: mine(), mediaItems: [] });
        const update = capture('put', 'Collection', ok(mine({ name: 'Space', visibilityLevel: 2 })));
        server.use(update.handler);
        const { user, onCollectionEdited } = renderDetails();
        await screen.findByRole('heading', { name: 'Sci-fi' });

        await user.click(screen.getByTestId('EditIcon').closest('button')!);
        const dialog = await screen.findByRole('dialog', { name: 'Edit Collection' });
        // Filled in by an effect once the dialog is open
        await waitFor(() => expect(within(dialog).getByLabelText('Collection Name')).toHaveValue('Sci-fi'));
        await user.clear(within(dialog).getByLabelText('Collection Name'));
        await user.type(within(dialog).getByLabelText('Collection Name'), 'Space');
        await user.click(within(dialog).getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Followers Only' }));
        await user.click(within(dialog).getByRole('button', { name: 'Save Changes' }));

        expect(await screen.findByRole('heading', { name: 'Space' })).toBeInTheDocument();
        expect(update.last.body).toEqual({ collectionID: 5, name: 'Space', visibilityLevel: 2 });
        expect(onCollectionEdited).toHaveBeenCalled();
    });

    describe('deleting the collection', () => {
        it('deletes after confirming', async () => {
            const confirm = stubConfirm(true);
            serveDetails({ collection: mine(), mediaItems: [] });
            const remove = capture('delete', 'Collection/5', noContent());
            server.use(remove.handler);
            const { user, onCollectionDeleted } = renderDetails();
            await screen.findByRole('heading', { name: 'Sci-fi' });

            await user.click(screen.getByTestId('DeleteIcon').closest('button')!);

            await waitFor(() => expect(onCollectionDeleted).toHaveBeenCalled());
            expect(confirm).toHaveBeenCalledWith('Are you sure you want to delete this collection? This cannot be undone.');
        });

        it('keeps it when cancelled', async () => {
            stubConfirm(false);
            serveDetails({ collection: mine(), mediaItems: [] });
            const remove = capture('delete', 'Collection/5', noContent());
            server.use(remove.handler);
            const { user, onCollectionDeleted } = renderDetails();
            await screen.findByRole('heading', { name: 'Sci-fi' });

            await user.click(screen.getByTestId('DeleteIcon').closest('button')!);

            expect(remove.calls).toHaveLength(0);
            expect(onCollectionDeleted).not.toHaveBeenCalled();
        });

        it('shows why it failed', async () => {
            stubConfirm(true);
            serveDetails({ collection: mine(), mediaItems: [] });
            server.use(capture('delete', 'Collection/5', apiError(400, 'Favourites can\'t be deleted.')).handler);
            const { user } = renderDetails();
            await screen.findByRole('heading', { name: 'Sci-fi' });

            await user.click(screen.getByTestId('DeleteIcon').closest('button')!);

            expect(await screen.findByText('Favourites can\'t be deleted.')).toBeInTheDocument();
        });
    });

    describe('removing media', () => {
        it('removes a media and updates the count', async () => {
            serveStats();
            const arrival = movie({ title: 'Arrival', mediaType: 0, externalApiID: 'tmdb-1' });
            serveDetails({ collection: mine({ mediaCount: 2 }), mediaItems: [collectionMedia({ media: arrival }), collectionMedia({ media: movie({ title: 'Contact' }) })] });
            const remove = capture('delete', 'Collection/5/media/0-tmdb-1', noContent());
            server.use(remove.handler);
            const { user, onMediaRemoved } = renderDetails();
            await screen.findByText('Arrival');

            await user.click((await screen.findAllByRole('button', { name: 'Remove from Collection' }))[0]);

            await waitFor(() => expect(screen.queryByText('Arrival')).not.toBeInTheDocument());
            expect(screen.getByText(/• 1 items/)).toBeInTheDocument();
            expect(onMediaRemoved).toHaveBeenCalled();
        });

        it('shows why removing failed', async () => {
            serveStats();
            serveDetails({ collection: mine(), mediaItems: [collectionMedia({ media: movie({ title: 'Arrival', externalApiID: 'tmdb-1' }) })] });
            server.use(capture('delete', 'Collection/5/media/0-tmdb-1', apiError(500, 'Could not remove.')).handler);
            const { user, onMediaRemoved } = renderDetails();
            await screen.findByText('Arrival');

            await user.click(await screen.findByRole('button', { name: 'Remove from Collection' }));

            expect(await screen.findByText('Could not remove.')).toBeInTheDocument();
            expect(onMediaRemoved).not.toHaveBeenCalled();
        });
    });

    it('shows the loading state between collections', async () => {
        const response = deferred(() => ok({ collection: mine(), mediaItems: [] }));
        server.use(capture('get', 'Collection/:id', () => response.promise).handler);
        renderDetails();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByRole('heading', { name: 'Sci-fi' })).toBeInTheDocument();
    });
});
