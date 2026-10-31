import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { useNavigate } from 'react-router-dom';
import { MediaReviewPage } from '../../src/pages/MediaReviewPage';
import { server } from '../support/server';
import { apiError, capture } from '../support/api';
import { album, movie, review } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveMediaReviews } from '../support/handlers';
import { knownIssue } from '../support/known-issue';

const PATH = '/media/:mediaType/:externalApiId';

const arrival = movie({ externalApiID: 'tmdb-1', title: 'Arrival', overview: 'Aliens arrive.' });

describe('MediaDetails', () => {
    function renderDetails(search = '', media = arrival) {
        return renderWithApp(<MediaReviewPage />, { path: PATH, route: { pathname: `/media/${media.mediaType}/${media.externalApiID}`, search, state: { media } } as any });
    }

    it('shows the media and its public reviews', async () => {
        const reviews = serveMediaReviews([review({ username: 'bob', reviewText: 'Loved it.', score: 9 })], { totalCount: 1 });
        renderDetails();

        expect(screen.getByText('Aliens arrive.')).toBeInTheDocument();
        expect(await screen.findByText('Loved it.')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Reviews (1)' })).toBeInTheDocument();
        expect(Object.fromEntries(reviews.last.url.searchParams)).toEqual({ page: '1', sortBy: 'created_desc', minScore: '1', maxScore: '10' });
        expect(reviews.last.url.pathname).toBe('/api/Review/media/0/tmdb-1');
    });

    it('shows the artist of an album', async () => {
        serveMediaReviews([]);
        renderDetails('', album({ creator: 'Daft Punk' }));

        expect(screen.getByRole('heading', { name: 'Creator' })).toBeInTheDocument();
        expect(screen.getByText('Daft Punk')).toBeInTheDocument();
        await screen.findByText('No public reviews yet. Be the first!');
    });

    it('reads the filters and page from the URL', async () => {
        const reviews = serveMediaReviews([review()], { totalPages: 3 });
        renderDetails('?page=2&sortBy=score_desc&minScore=4.5&maxScore=9&hasWrittenText=true');

        await screen.findByText('Really good.');
        expect(Object.fromEntries(reviews.last.url.searchParams)).toEqual({
            page: '2', sortBy: 'score_desc', minScore: '4.5', maxScore: '9', hasWrittenText: 'true',
        });
        expect(screen.getByText('Score Range: 4.5 - 9')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).toBeChecked();
    });

    it('applies the filters from page 1, keeping the media in the router state', async () => {
        const reviews = serveMediaReviews([review()], { totalPages: 2 });
        const { user } = renderDetails('?tab=details&page=2');
        await screen.findByText('Really good.');

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Lowest Score' }));
        await user.click(screen.getByRole('switch', { name: 'Only show written reviews' }));
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(currentLocation()).toBe('/media/0/tmdb-1?sortBy=score_asc&minScore=1&maxScore=10&hasWrittenText=true&page=1');
        await waitFor(() => expect(reviews.last.url.searchParams.get('sortBy')).toBe('score_asc'));
        // Still rendered from the state, not refetched
        expect(screen.getByRole('heading', { name: 'Arrival' })).toBeInTheDocument();
    });

    it('moves the score range with the keyboard', async () => {
        serveMediaReviews([review()]);
        const { user } = renderDetails();
        await screen.findByText('Really good.');

        const [low] = screen.getAllByRole('slider');
        low.focus();
        await user.keyboard('{ArrowRight}{ArrowRight}');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(screen.getByText('Score Range: 2 - 10')).toBeInTheDocument();
        expect(currentLocation()).toContain('minScore=2&maxScore=10');
    });

    it('clears the filters back to the details tab', async () => {
        serveMediaReviews([review()]);
        const { user } = renderDetails('?sortBy=score_desc&minScore=5&hasWrittenText=true');
        await screen.findByText('Really good.');

        await user.click(screen.getByRole('button', { name: 'Clear' }));

        expect(currentLocation()).toBe('/media/0/tmdb-1?tab=details');
        expect(screen.getByText('Score Range: 1 - 10')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).not.toBeChecked();
    });

    it('pages through the reviews', async () => {
        serveMediaReviews([review()], { totalPages: 3 });
        const { user } = renderDetails();
        await screen.findByText('Really good.');

        await user.click(screen.getAllByRole('button', { name: 'Go to page 2' })[0]);

        expect(currentLocation()).toBe('/media/0/tmdb-1?page=2');
    });

    it('shows the error of the review list', async () => {
        server.use(capture('get', 'Review/media/:t/:id', apiError(500, 'Reviews are down.')).handler);
        renderDetails();

        expect(await screen.findByText('Reviews are down.')).toBeInTheDocument();
    });

    knownIssue('W11', 'a filter that matches nothing can still be changed or cleared', async () => {
        serveMediaReviews([]);
        renderDetails('?minScore=9.5');
        await screen.findByText('No public reviews yet. Be the first!');

        // The filter is disabled whenever the list is empty, so the user is stuck with it
        expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
    });

    knownIssue('W11', 'the filter panel follows the URL (e.g. the browser\'s Back button)', async () => {
        function Back() {
            const navigate = useNavigate();
            return <button onClick={() => navigate(-1)}>back</button>;
        }
        serveMediaReviews([review()]);
        const { user } = renderWithApp(<><MediaReviewPage /><Back /></>, {
            path: PATH, route: { pathname: '/media/0/tmdb-1', state: { media: arrival } },
        });
        await screen.findByText('Really good.');
        await user.click(screen.getByRole('switch', { name: 'Only show written reviews' }));
        await user.click(screen.getByRole('button', { name: 'Apply' }));
        await waitFor(() => expect(currentLocation()).toContain('hasWrittenText=true'));

        await user.click(screen.getByRole('button', { name: 'back' }));

        await waitFor(() => expect(currentLocation()).toBe('/media/0/tmdb-1'));
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).not.toBeChecked();
    });

    it('shows the reviews with their author, dates and verdict', async () => {
        serveMediaReviews([review({ username: 'bob', score: 3.5, createdAt: '2026-03-10T12:00:00Z', updatedAt: '2026-04-02T08:00:00Z', pros: 'Music', cons: null, reviewText: null })]);
        const { user } = renderDetails();

        expect(await screen.findByText('Created: 3/10/2026')).toBeInTheDocument();
        expect(screen.getByText('Updated: 4/2/2026')).toBeInTheDocument();
        expect(screen.getByText('3.5/10')).toBeInTheDocument();
        expect(screen.getByText('Poor')).toBeInTheDocument();
        expect(screen.getByText('No written review.')).toBeInTheDocument();
        expect(screen.getByText('Music')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /bob/ }));
        expect(currentLocation()).toBe('/profile/bob');
    });
});
