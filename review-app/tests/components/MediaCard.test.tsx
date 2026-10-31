import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { MediaCard } from '../../src/components/MediaCard';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { album, authUser, collection, favourites, movie } from '../support/builders';
import { currentLocation, currentLocationState, renderWithApp } from '../support/render';
import { serveStats, serveUserCollections } from '../support/handlers';
import { knownIssue } from '../support/known-issue';

const arrival = movie({ externalApiID: 'tmdb-1', title: 'Arrival', releaseDate: '2016-11-11', overview: 'Aliens arrive.' });

describe('MediaCard', () => {
    it('shows the media and its average score', async () => {
        const stats = serveStats({ averageScore: 8.5, reviewCount: 4 });
        renderWithApp(<MediaCard media={arrival} />);

        expect(screen.getByText('Arrival')).toBeInTheDocument();
        expect(screen.getByText('2016-11-11')).toBeInTheDocument();
        expect(screen.getByText('Aliens arrive.')).toBeInTheDocument();
        expect(screen.getByRole('img', { name: 'Arrival' })).toHaveAttribute('src', arrival.posterUrl);
        expect(await screen.findByText('8.5 / 10')).toBeInTheDocument();
        expect(screen.getByText('4 reviews')).toBeInTheDocument();
        expect(Object.fromEntries(stats.last.url.searchParams)).toEqual({ externalApiId: 'tmdb-1', mediaType: '0' });
    });

    it.each([
        [{ averageScore: 0, reviewCount: 0 }, '-- / 10', '0 reviews'],
        [{ averageScore: 6, reviewCount: 1 }, '6 / 10', '1 review'],
    ])('shows %j as %s', async (stats, score, count) => {
        serveStats(stats);
        renderWithApp(<MediaCard media={arrival} />);

        expect(await screen.findByText(score)).toBeInTheDocument();
        expect(screen.getByText(count)).toBeInTheDocument();
    });

    it('falls back for missing data', async () => {
        serveStats();
        renderWithApp(<MediaCard media={movie({ title: 'Untitled', posterUrl: undefined, releaseDate: undefined, overview: undefined })} />);

        expect(screen.getByText('Unknown Year')).toBeInTheDocument();
        expect(screen.getByText('No overview available for this title.')).toBeInTheDocument();
        expect(screen.getByRole('img', { name: 'Untitled' }).getAttribute('src')).toContain('placehold.co');
        await screen.findByText('-- / 10');
    });

    it('shows the artist for music', async () => {
        serveStats();
        renderWithApp(<MediaCard media={album({ creator: 'Daft Punk', overview: 'not shown' })} />);

        expect(screen.getByText('Daft Punk')).toBeInTheDocument();
        expect(screen.queryByText('not shown')).not.toBeInTheDocument();
        await screen.findByText('-- / 10');
    });

    it('shows a loading state, then the stats error', async () => {
        const response = deferred(() => apiError(500, 'Stats are down'));
        server.use(capture('get', 'Review/stats/average-score', () => response.promise).handler);
        renderWithApp(<MediaCard media={arrival} />);

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('Stats are down')).toBeInTheDocument();
    });

    it('opens the media page with the media in the router state', async () => {
        serveStats();
        const { user } = renderWithApp(<MediaCard media={arrival} />);

        await user.click(screen.getByText('Arrival'));

        expect(currentLocation()).toBe('/media/0/tmdb-1');
        expect(currentLocationState()).toEqual({ media: arrival });
    });

    it('opens the media page from the poster too', async () => {
        serveStats();
        const { user } = renderWithApp(<MediaCard media={arrival} />);

        await user.click(screen.getByRole('img', { name: 'Arrival' }));

        expect(currentLocation()).toBe('/media/0/tmdb-1');
    });

    it('has no collection buttons for a visitor', async () => {
        serveStats();
        renderWithApp(<MediaCard media={arrival} />, { user: null });
        await screen.findByText('-- / 10');

        expect(screen.queryByRole('button', { name: 'Add to Collection' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Add to Favorites' })).not.toBeInTheDocument();
    });

    describe('logged in', () => {
        const alice = authUser({ username: 'alice' });

        it('adds the media to a chosen collection', async () => {
            serveStats();
            const collections = serveUserCollections([favourites({ id: 1 }), collection({ id: 2, name: 'Sci-fi' })]);
            const add = capture('post', 'Collection/2/media', ok({ message: 'Added' }));
            server.use(add.handler);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Collection' }));
            await user.click(await screen.findByRole('menuitem', { name: 'Sci-fi' }));

            expect(await screen.findByText('Added to collection!')).toBeInTheDocument();
            expect(add.last.body).toEqual({ type: 0, externalApiID: 'tmdb-1' });
            expect(collections.last.url.pathname).toBe('/api/User/alice/collections');
        });

        it('loads the collections only once', async () => {
            serveStats();
            const collections = serveUserCollections([collection({ id: 2, name: 'Sci-fi' })]);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Collection' }));
            await screen.findByRole('menuitem', { name: 'Sci-fi' });
            await user.keyboard('{Escape}');
            await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
            await user.click(screen.getByRole('button', { name: 'Add to Collection' }));

            await screen.findByRole('menuitem', { name: 'Sci-fi' });
            expect(collections.calls).toHaveLength(1);
        });

        it('says when there are no collections', async () => {
            serveStats();
            serveUserCollections([]);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Collection' }));

            expect(await screen.findByRole('menuitem', { name: 'No collections found.' })).toHaveAttribute('aria-disabled', 'true');
        });

        it('shows why adding failed', async () => {
            serveStats();
            serveUserCollections([collection({ id: 2, name: 'Sci-fi' })]);
            server.use(capture('post', 'Collection/2/media', apiError(409, 'This media is already in the collection.')).handler);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Collection' }));
            await user.click(await screen.findByRole('menuitem', { name: 'Sci-fi' }));

            expect(await screen.findByText('This media is already in the collection.')).toBeInTheDocument();
        });

        it('adds the media to the Favourites collection', async () => {
            vi.spyOn(console, 'log').mockImplementation(() => {});
            serveStats();
            serveUserCollections([collection({ id: 2, name: 'Sci-fi' }), favourites({ id: 7 })]);
            const add = capture('post', 'Collection/7/media', ok({ message: 'Added' }));
            server.use(add.handler);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Favorites' }));

            expect(await screen.findByText('Added to Favourites!')).toBeInTheDocument();
            expect(add.calls).toHaveLength(1);
        });

        it('explains a missing Favourites collection', async () => {
            serveStats();
            serveUserCollections([collection({ name: 'Sci-fi' })]);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Favorites' }));

            expect(await screen.findByText('Could not find your Favourites collection.')).toBeInTheDocument();
        });

        it('shows why adding to Favourites failed', async () => {
            vi.spyOn(console, 'log').mockImplementation(() => {});
            serveStats();
            serveUserCollections([favourites({ id: 7 })]);
            server.use(capture('post', 'Collection/7/media', apiError(409, 'Already a favourite.')).handler);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Favorites' }));

            expect(await screen.findByText('Already a favourite.')).toBeInTheDocument();
        });

        knownIssue('W9', 'adding to Favourites leaves no debug output in the console', async () => {
            const log = vi.spyOn(console, 'log').mockImplementation(() => {});
            serveStats();
            serveUserCollections([favourites({ id: 7 })]);
            server.use(capture('post', 'Collection/7/media', ok({})).handler);
            const { user } = renderWithApp(<MediaCard media={arrival} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Add to Favorites' }));
            await screen.findByText('Added to Favourites!');

            expect(log).not.toHaveBeenCalled();
        });

        it('offers removal instead of adding inside the owner\'s collection', async () => {
            serveStats();
            const onRemove = vi.fn();
            const { user } = renderWithApp(<MediaCard media={arrival} collectionId={3} isOwner onRemove={onRemove} />, { user: alice });

            await user.click(await screen.findByRole('button', { name: 'Remove from Collection' }));

            expect(onRemove).toHaveBeenCalledWith(0, 'tmdb-1');
            const card = screen.getByText('Arrival').closest('.MuiCard-root') as HTMLElement;
            expect(within(card).queryByRole('button', { name: 'Add to Collection' })).not.toBeInTheDocument();
        });

        it('offers adding in someone else\'s collection', async () => {
            serveStats();
            renderWithApp(<MediaCard media={arrival} collectionId={3} isOwner={false} onRemove={vi.fn()} />, { user: alice });

            expect(await screen.findByRole('button', { name: 'Add to Collection' })).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Remove from Collection' })).not.toBeInTheDocument();
        });
    });
});
