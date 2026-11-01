import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { useNavigate } from 'react-router-dom';
import { CollectionsTab } from '../../src/components/CollectionsTab';
import { server } from '../support/server';
import { apiError, capture, noContent, ok } from '../support/api';
import { authUser, collection, collectionMedia, favourites, movie } from '../support/builders';
import { renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';
import { stubConfirm } from '../support/browser';
import { knownIssue } from '../support/known-issue';

const PATH = '/profile/:username/collections';

function serveCollections(byUser: Record<string, ReturnType<typeof collection>[]>) {
    const endpoint = capture('get', 'User/:username/collections', (_req, params) => HttpResponse.json(byUser[params.username as string] ?? []));
    server.use(endpoint.handler);
    return endpoint;
}

function serveDetails(details: Record<number, { collection: ReturnType<typeof collection>; mediaItems: ReturnType<typeof collectionMedia>[] }>) {
    const endpoint = capture('get', 'Collection/:id', (_req, params) => {
        const found = details[Number(params.id)];
        return found ? HttpResponse.json(found) : HttpResponse.json({ error: 'Collection not found.' }, { status: 404 });
    });
    server.use(endpoint.handler);
    return endpoint;
}

const listItem = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}`) });

describe('CollectionsTab', () => {
    it('lists the collections and opens the first one', async () => {
        serveStats();
        const fav = favourites({ id: 1, mediaCount: 1, isOwner: true });
        const scifi = collection({ id: 2, name: 'Sci-fi', mediaCount: 0, isOwner: true });
        const list = serveCollections({ bob: [fav, scifi] });
        serveDetails({
            1: { collection: fav, mediaItems: [collectionMedia({ media: movie({ title: 'Arrival' }) })] },
            2: { collection: scifi, mediaItems: [] },
        });
        const { user } = renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/bob/collections' });

        expect(await screen.findByRole('heading', { name: 'Favourites' })).toBeInTheDocument();
        expect(screen.getByText('Arrival')).toBeInTheDocument();
        expect(listItem('Favourites')).toHaveTextContent('1 items');
        expect(list.last.url.searchParams.get('sortBy')).toBe('createdAt_asc');

        await user.click(listItem('Sci-fi'));
        expect(await screen.findByRole('heading', { name: 'Sci-fi' })).toBeInTheDocument();
        expect(screen.getByText('This collection is empty.')).toBeInTheDocument();
    });

    it('says when there are no collections', async () => {
        serveCollections({ bob: [] });
        renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/bob/collections' });

        expect(await screen.findByText('No collections yet.')).toBeInTheDocument();
        expect(screen.getByText('Select a collection to view its contents.')).toBeInTheDocument();
    });

    it('shows the error', async () => {
        server.use(capture('get', 'User/:username/collections', apiError(404, 'User not found.')).handler);
        renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/nobody/collections' });

        expect(await screen.findByText('User not found.')).toBeInTheDocument();
    });

    it('sorts with the picker', async () => {
        const list = serveCollections({ bob: [collection({ id: 3, name: 'Only' })] });
        serveDetails({ 3: { collection: collection({ id: 3, name: 'Only' }), mediaItems: [] } });
        const { user } = renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/bob/collections' });
        await screen.findByRole('heading', { name: 'Only' });

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Name (Z-A)' }));

        await waitFor(() => expect(list.last.url.searchParams.get('sortBy')).toBe('name_desc'));
    });

    it('lets only the owner create a collection, then opens it', async () => {
        const mine = [collection({ id: 3, name: 'Old', isOwner: true })];
        const created = collection({ id: 9, name: 'Brand new', isOwner: true });
        let afterCreate = false;
        server.use(capture('get', 'User/:username/collections', () => HttpResponse.json(afterCreate ? [...mine, created] : mine)).handler);
        serveDetails({ 3: { collection: mine[0], mediaItems: [] }, 9: { collection: created, mediaItems: [] } });
        const create = capture('post', 'Collection', () => { afterCreate = true; return ok({ id: 9 }); });
        server.use(create.handler);
        const { user } = renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/alice/collections', user: authUser({ username: 'alice' }) });
        await screen.findByRole('heading', { name: 'Old' });

        await user.click(screen.getByTestId('AddIcon').closest('button')!);
        const dialog = await screen.findByRole('dialog', { name: 'Create New Collection' });
        await user.type(within(dialog).getByLabelText('Collection Name'), 'Brand new');
        await user.click(within(dialog).getByRole('button', { name: 'Create' }));

        expect(await screen.findByRole('heading', { name: 'Brand new' })).toBeInTheDocument();
        expect(create.last.body).toEqual({ name: 'Brand new', visibilityLevel: 0 });
    });

    it('has no create button for visitors', async () => {
        serveCollections({ bob: [collection({ id: 3, isOwner: false })] });
        serveDetails({ 3: { collection: collection({ id: 3, isOwner: false }), mediaItems: [] } });
        renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/bob/collections' });
        await screen.findByText('This collection is empty.');

        expect(screen.queryByTestId('AddIcon')).not.toBeInTheDocument();
    });

    it('opens nothing after the selected collection is deleted', async () => {
        stubConfirm(true);
        let deleted = false;
        const doomed = collection({ id: 3, name: 'Doomed', isOwner: true });
        server.use(capture('get', 'User/:username/collections', () => HttpResponse.json(deleted ? [] : [doomed])).handler);
        serveDetails({ 3: { collection: doomed, mediaItems: [] } });
        server.use(capture('delete', 'Collection/3', () => { deleted = true; return noContent(); }).handler);
        const { user } = renderWithApp(<CollectionsTab />, { path: PATH, route: '/profile/alice/collections', user: authUser({ username: 'alice' }) });
        await screen.findByRole('heading', { name: 'Doomed' });

        await user.click(screen.getByTestId('DeleteIcon').closest('button')!);

        expect(await screen.findByText('No collections yet.')).toBeInTheDocument();
        expect(screen.getByText('Select a collection to view its contents.')).toBeInTheDocument();
    });

    knownIssue('W13', 'another user\'s profile opens their own first collection', async () => {
        function ToCarol() {
            const navigate = useNavigate();
            return <button onClick={() => navigate('/profile/carol/collections')}>carol</button>;
        }
        const bobs = collection({ id: 3, name: 'Bob\'s list' });
        const carols = collection({ id: 4, name: 'Carol\'s list' });
        serveCollections({ bob: [bobs], carol: [carols] });
        serveDetails({ 3: { collection: bobs, mediaItems: [] }, 4: { collection: carols, mediaItems: [] } });
        const { user } = renderWithApp(<><CollectionsTab /><ToCarol /></>, { path: PATH, route: '/profile/bob/collections' });
        await screen.findByRole('heading', { name: 'Bob\'s list' });

        await user.click(screen.getByRole('button', { name: 'carol' }));

        expect(await screen.findByRole('heading', { name: 'Carol\'s list' })).toBeInTheDocument();
    });
});
