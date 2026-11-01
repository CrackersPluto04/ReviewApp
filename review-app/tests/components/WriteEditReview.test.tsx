import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { WriteEditReview } from '../../src/components/WriteEditReview';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser, movie } from '../support/builders';
import { renderWithApp } from '../support/render';
import { knownIssue } from '../support/known-issue';

const arrival = movie({ externalApiID: 'tmdb-1', title: 'Arrival' });

function serveExisting(reviewData: Record<string, any> | null) {
    const check = capture('get', 'Review/check', ok({ hasReviewed: !!reviewData, reviewData }));
    server.use(check.handler);
    return check;
}

function renderForm() {
    return renderWithApp(<WriteEditReview media={arrival} />, { user: authUser() });
}

const field = (label: string) => screen.getByRole('textbox', { name: label });

describe('WriteEditReview', () => {
    it('checks for an existing review of the media', async () => {
        const check = serveExisting(null);
        renderForm();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        expect(await screen.findByRole('heading', { name: 'Write your Review' })).toBeInTheDocument();
        expect(Object.fromEntries(check.last.url.searchParams)).toEqual({ externalApiId: 'tmdb-1', mediaType: '0' });
        expect(screen.getByText('5 / 10')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Save Review' })).toBeEnabled();
    });

    it('creates a review with every field', async () => {
        serveExisting(null);
        const create = capture('post', 'Review', ok({ message: 'Review saved!' }));
        server.use(create.handler);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });

        screen.getByRole('slider').focus();
        await user.keyboard('{ArrowRight}{ArrowRight}{ArrowRight}');
        await user.type(field('What did you think?'), 'Smart and moving.');
        await user.type(field('Pros'), 'Acting');
        await user.type(field('Cons'), 'Slow start');
        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Followers only' }));
        await user.click(screen.getByRole('button', { name: 'Save Review' }));

        expect(await screen.findByText('Review saved!')).toBeInTheDocument();
        expect(create.last.body).toEqual({
            mediaDto: arrival,
            reviewDto: { score: 6.5, reviewText: 'Smart and moving.', pros: 'Acting', cons: 'Slow start', visibilityLevel: 2 },
        });
        // The next save edits it
        expect(screen.getByRole('heading', { name: 'Edit your Review' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Update Review' })).toBeInTheDocument();
    });

    it('fills in an existing review and updates it', async () => {
        serveExisting({ score: 9, reviewText: 'Great.', pros: 'Story', cons: null, visibilityLevel: 0 });
        const edit = capture('put', 'Review', ok({ message: 'Review updated!' }));
        server.use(edit.handler);
        const { user } = renderForm();

        expect(await screen.findByRole('heading', { name: 'Edit your Review' })).toBeInTheDocument();
        expect(screen.getByText('9 / 10')).toBeInTheDocument();
        expect(field('What did you think?')).toHaveValue('Great.');
        expect(field('Pros')).toHaveValue('Story');
        expect(field('Cons')).toHaveValue('');
        expect(screen.getByRole('combobox')).toHaveTextContent('Private');

        await user.clear(field('What did you think?'));
        await user.type(field('What did you think?'), 'Even better the second time.');
        await user.click(screen.getByRole('button', { name: 'Update Review' }));

        expect(await screen.findByText('Review updated!')).toBeInTheDocument();
        expect(edit.last.body.reviewDto).toEqual({ score: 9, reviewText: 'Even better the second time.', pros: 'Story', cons: '', visibilityLevel: 0 });
    });

    it('counts characters and blocks saving over the limits', async () => {
        serveExisting(null);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });

        await user.click(field('Pros'));
        await user.paste('x'.repeat(251));

        expect(screen.getByText('251 / 250')).toBeInTheDocument();
        expect(field('Pros')).toHaveAttribute('aria-invalid', 'true');
        expect(screen.getByRole('button', { name: 'Save Review' })).toBeDisabled();

        await user.type(field('Pros'), '{Backspace}');
        expect(screen.getByRole('button', { name: 'Save Review' })).toBeEnabled();

        await user.click(field('What did you think?'));
        await user.paste('y'.repeat(501));
        expect(screen.getByText('501 / 500')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Save Review' })).toBeDisabled();
    });

    it('shows a spinner on the button while saving', async () => {
        serveExisting(null);
        const response = deferred(() => ok({ message: 'Saved' }));
        server.use(capture('post', 'Review', () => response.promise).handler);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });

        await user.click(screen.getByRole('button', { name: 'Save Review' }));

        await waitFor(() => expect(screen.queryByRole('button', { name: 'Save Review' })).not.toBeInTheDocument());
        response.release();
        expect(await screen.findByText('Saved')).toBeInTheDocument();
    });

    it('shows why the check failed', async () => {
        server.use(capture('get', 'Review/check', apiError(500, 'Could not check your review.')).handler);
        renderForm();

        expect(await screen.findByText('Could not check your review.')).toBeInTheDocument();
    });

    it('shows why saving a new review failed', async () => {
        serveExisting(null);
        server.use(capture('post', 'Review', apiError(400, 'Score must be between 1 and 10.')).handler);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });

        await user.click(screen.getByRole('button', { name: 'Save Review' }));

        expect(await screen.findByText('Score must be between 1 and 10.')).toBeInTheDocument();
    });

    knownIssue('W7', 'a failed create stays a create (the next save is not an edit)', async () => {
        serveExisting(null);
        server.use(capture('post', 'Review', apiError(500, 'Server error')).handler);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });

        await user.click(screen.getByRole('button', { name: 'Save Review' }));
        await screen.findByText('Server error');

        expect(screen.getByRole('heading', { name: 'Write your Review' })).toBeInTheDocument();
    });

    knownIssue('W7', 'a failed update says why', async () => {
        serveExisting({ score: 9, reviewText: 'Great.', visibilityLevel: 1 });
        server.use(capture('put', 'Review', apiError(400, 'Review text is too long.')).handler);
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Edit your Review' });

        await user.click(screen.getByRole('button', { name: 'Update Review' }));

        expect(await screen.findByText('Review text is too long.')).toBeInTheDocument();
    });

    knownIssue('W7', 'a successful save after a failure is shown as a success', async () => {
        serveExisting(null);
        let fail = true;
        server.use(
            capture('post', 'Review', () => fail ? apiError(500, 'Server error') : ok({ message: 'Saved' })).handler,
            capture('put', 'Review', ok({ message: 'Saved' })).handler,
        );
        const { user } = renderForm();
        await screen.findByRole('heading', { name: 'Write your Review' });
        await user.click(screen.getByRole('button', { name: /Save Review|Update Review/ }));
        await screen.findByText('Server error');

        fail = false;
        await user.click(screen.getByRole('button', { name: /Save Review|Update Review/ }));

        const toast = await screen.findByText('Saved');
        expect(toast.closest('.MuiAlert-root')).toHaveClass('MuiAlert-colorSuccess');
    });
});
