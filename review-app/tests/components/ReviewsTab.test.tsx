import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { ReviewsTab } from '../../src/components/ReviewsTab';
import { server } from '../support/server';
import { apiError, capture, noContent } from '../support/api';
import { paged, profileReview } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubAlert, stubConfirm } from '../support/browser';

const PATH = '/profile/:username/reviews';

function serveReviews(respond: (req: { url: URL }) => Response) {
    const endpoint = capture('get', 'User/:username/reviews', respond);
    server.use(endpoint.handler);
    return endpoint;
}

const list = (items: ReturnType<typeof profileReview>[], overrides: Record<string, any> = {}) =>
    HttpResponse.json(paged(items, overrides));

function renderTab(search = '') {
    return renderWithApp(<ReviewsTab />, { path: PATH, route: `/profile/bob/reviews${search}` });
}

describe('ReviewsTab', () => {
    it('lists the user\'s reviews with the default filters', async () => {
        const reviews = serveReviews(() => list([profileReview({ title: 'Arrival' })], { totalCount: 12 }));
        renderTab();

        expect(await screen.findByText('Arrival')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Reviews (12)' })).toBeInTheDocument();
        expect(reviews.last.url.pathname).toBe('/api/User/bob/reviews');
        expect(Object.fromEntries(reviews.last.url.searchParams)).toEqual({ page: '1', sortBy: 'created_desc', minScore: '1', maxScore: '10' });
    });

    it('says when nothing matches', async () => {
        serveReviews(() => list([]));
        renderTab('?minScore=9');

        expect(await screen.findByText('No reviews found matching these filters.')).toBeInTheDocument();
        // The filter stays usable, so the user can widen it again
        expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
    });

    it('shows the error', async () => {
        serveReviews(() => apiError(404, 'User not found.'));
        renderTab();

        expect(await screen.findByText('User not found.')).toBeInTheDocument();
    });

    it('applies the filters from page 1 and drops the written-text filter when switched off', async () => {
        const reviews = serveReviews(() => list([profileReview()], { totalPages: 2 }));
        const { user } = renderTab('?page=2&hasWrittenText=true');
        await screen.findByText('Really good.');

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Highest Score' }));
        // The filter panel starts from the URL
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).toBeChecked();
        await user.click(screen.getByRole('switch', { name: 'Only show written reviews' }));
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(currentLocation()).toBe('/profile/bob/reviews?page=1&sortBy=score_desc&minScore=1&maxScore=10');
        await waitFor(() => expect(reviews.last.url.searchParams.get('sortBy')).toBe('score_desc'));
    });

    it('clears every filter', async () => {
        serveReviews(() => list([profileReview()]));
        const { user } = renderTab('?sortBy=score_asc&minScore=3');
        await screen.findByText('Really good.');

        await user.click(screen.getByRole('button', { name: 'Clear' }));

        expect(currentLocation()).toBe('/profile/bob/reviews');
    });

    it('pages through the reviews', async () => {
        serveReviews(req => list([profileReview({ title: `Page ${req.url.searchParams.get('page')}` })], { totalPages: 3 }));
        const { user } = renderTab('?sortBy=score_asc');
        await screen.findByText('Page 1');

        await user.click(screen.getAllByRole('button', { name: 'Go to page 3' })[0]);

        expect(await screen.findByText('Page 3')).toBeInTheDocument();
        expect(currentLocation()).toBe('/profile/bob/reviews?sortBy=score_asc&page=3');
    });

    describe('deleting', () => {
        it('deletes the owner\'s review after confirming', async () => {
            const confirm = stubConfirm(true);
            serveReviews(() => list([profileReview({ id: 5, title: 'Arrival', isOwner: true }), profileReview({ title: 'Contact' })], { totalCount: 2 }));
            const remove = capture('delete', 'Review/5', noContent());
            server.use(remove.handler);
            const { user } = renderTab();
            await screen.findByText('Arrival');

            await user.click(screen.getByRole('button', { name: 'Delete Review' }));

            await waitFor(() => expect(screen.queryByText('Arrival')).not.toBeInTheDocument());
            expect(screen.getByText('Contact')).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: 'Reviews (1)' })).toBeInTheDocument();
            expect(confirm).toHaveBeenCalledWith('Are you sure you want to delete this review? This cannot be undone!');
            expect(remove.calls).toHaveLength(1);
        });

        it('keeps the review when the user cancels', async () => {
            stubConfirm(false);
            serveReviews(() => list([profileReview({ id: 5, title: 'Arrival', isOwner: true })]));
            const remove = capture('delete', 'Review/5', noContent());
            server.use(remove.handler);
            const { user } = renderTab();
            await screen.findByText('Arrival');

            await user.click(screen.getByRole('button', { name: 'Delete Review' }));

            expect(screen.getByText('Arrival')).toBeInTheDocument();
            expect(remove.calls).toHaveLength(0);
        });

        it('says why the delete failed', async () => {
            stubConfirm(true);
            const alert = stubAlert();
            serveReviews(() => list([profileReview({ id: 5, title: 'Arrival', isOwner: true })]));
            server.use(capture('delete', 'Review/5', apiError(403, 'Not your review.')).handler);
            const { user } = renderTab();
            await screen.findByText('Arrival');

            await user.click(screen.getByRole('button', { name: 'Delete Review' }));

            await waitFor(() => expect(alert).toHaveBeenCalledWith('Not your review.'));
            expect(screen.getByText('Arrival')).toBeInTheDocument();
        });
    });
});
