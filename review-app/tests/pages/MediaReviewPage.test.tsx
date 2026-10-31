import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { useNavigate } from 'react-router-dom';
import { MediaReviewPage } from '../../src/pages/MediaReviewPage';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser, movie, review } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveMediaReviews } from '../support/handlers';
import { muteConsoleError } from '../support/browser';
import { knownIssue } from '../support/known-issue';

const PATH = '/media/:mediaType/:externalApiId';

const arrival = movie({ externalApiID: 'tmdb-1', title: 'Arrival', overview: 'Aliens arrive.' });

function serveDetails(respond: Response | (() => Response | Promise<Response>)) {
    const endpoint = capture('get', 'Media/media/:mediaType/:externalApiId', typeof respond === 'function' ? respond : respond);
    server.use(endpoint.handler);
    return endpoint;
}

function serveNoReview() {
    server.use(capture('get', 'Review/check', ok({ hasReviewed: false, reviewData: null })).handler);
}

describe('MediaReviewPage', () => {
    it('shows the media from the router state without fetching it', async () => {
        const details = serveDetails(ok(arrival));
        serveMediaReviews([]);
        renderWithApp(<MediaReviewPage />, { path: PATH, route: { pathname: '/media/0/tmdb-1', state: { media: arrival } } });

        expect(screen.getByRole('heading', { name: 'Arrival' })).toBeInTheDocument();
        expect(await screen.findByText('No public reviews yet. Be the first!')).toBeInTheDocument();
        expect(details.calls).toHaveLength(0);
    });

    it('loads the media when opened from a link', async () => {
        const response = deferred(() => HttpResponse.json(arrival));
        const details = serveDetails(() => response.promise);
        serveMediaReviews([]);
        renderWithApp(<MediaReviewPage />, { path: PATH, route: '/media/movie/tmdb-1' });

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();

        expect(await screen.findByRole('heading', { name: 'Arrival' })).toBeInTheDocument();
        expect(details.last.url.pathname).toBe('/api/Media/media/movie/tmdb-1');
    });

    knownIssue('W10', 'a media that can\'t be loaded shows an error instead of crashing', async () => {
        muteConsoleError();
        serveDetails(apiError(404, 'Media not found.'));
        renderWithApp(<MediaReviewPage />, { path: PATH, route: '/media/movie/nope' });

        expect(await screen.findByText(/not found/i)).toBeInTheDocument();
    });

    knownIssue('W10', 'moving to another media (router state of the first) shows the new one', async () => {
        function NextMedia() {
            const navigate = useNavigate();
            return <button onClick={() => navigate('/media/0/tmdb-2')}>next</button>;
        }
        serveDetails(ok(movie({ externalApiID: 'tmdb-2', title: 'Contact' })));
        serveMediaReviews([]);
        const { user } = renderWithApp(<><MediaReviewPage /><NextMedia /></>, {
            path: PATH, route: { pathname: '/media/0/tmdb-1', state: { media: arrival } },
        });
        await screen.findByRole('heading', { name: 'Arrival' });

        await user.click(screen.getByRole('button', { name: 'next' }));

        expect(await screen.findByRole('heading', { name: 'Contact' })).toBeInTheDocument();
    });

    it('opens the review tab and keeps the media in the router state', async () => {
        serveMediaReviews([]);
        serveNoReview();
        const { user } = renderWithApp(<MediaReviewPage />, {
            path: PATH, user: authUser(), route: { pathname: '/media/0/tmdb-1', state: { media: arrival } },
        });

        await user.click(screen.getByRole('tab', { name: 'Write/Edit Review' }));

        expect(currentLocation()).toBe('/media/0/tmdb-1?tab=review');
        expect(await screen.findByRole('heading', { name: 'Write your Review' })).toBeInTheDocument();
    });

    it('asks a visitor to log in to review', async () => {
        renderWithApp(<MediaReviewPage />, {
            path: PATH, user: null, route: { pathname: '/media/0/tmdb-1', search: '?tab=review', state: { media: arrival } } as any,
        });

        expect(await screen.findByText('Join the community to log your review for Arrival!')).toBeInTheDocument();
    });
});
