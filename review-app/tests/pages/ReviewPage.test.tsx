import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { ReviewPage } from '../../src/pages/ReviewPage';
import { server } from '../support/server';
import { apiError, capture, deferred, noContent, ok } from '../support/api';
import { authUser, profileReview, reply } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveReplies } from '../support/handlers';
import { stubAlert, stubConfirm } from '../support/browser';
import { knownIssue } from '../support/known-issue';

const PATH = '/review/:reviewId';

function serveReview(response: Response | (() => Promise<Response>)) {
    const endpoint = capture('get', 'Review/:id', typeof response === 'function' ? response : response);
    server.use(endpoint.handler);
    return endpoint;
}

function serveContext(id: number, response: Response) {
    const endpoint = capture('get', `ReviewReply/${id}`, response);
    server.use(endpoint.handler);
    return endpoint;
}

describe('ReviewPage', () => {
    it('shows the review with its author', async () => {
        const single = serveReview(ok(profileReview({ id: 12, username: 'bob', title: 'Arrival' })));
        const { user } = renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12' });

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        expect(await screen.findByRole('heading', { name: "bob's review" })).toBeInTheDocument();
        expect(screen.getByText('Arrival')).toBeInTheDocument();
        expect(single.last.url.pathname).toBe('/api/Review/12');

        await user.click(screen.getByText('bob'));
        expect(currentLocation()).toBe('/profile/bob');
    });

    it.each([
        ['an id that is not a number', '/review/abc', null],
        ['a missing or hidden review', '/review/12', apiError(404, 'Review not found.')],
    ])('explains %s', async (_, route, response) => {
        if (response) serveReview(response);
        const { user } = renderWithApp(<ReviewPage />, { path: PATH, route });

        expect(await screen.findByText("This review doesn't exist or isn't visible to you.")).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Go to the home page' }));
        expect(currentLocation()).toBe('/home');
    });

    it('shows other errors as they are', async () => {
        serveReview(apiError(500, 'Reviews are down.'));
        renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12' });

        expect(await screen.findByText('Reviews are down.')).toBeInTheDocument();
    });

    describe('opened at a reply (?reply=)', () => {
        it('opens the thread at a layer 2 reply and highlights it', async () => {
            const target = reply({ id: 34, content: 'The reply from the notification' });
            serveReview(ok(profileReview({ id: 12, replyCount: 2 })));
            serveContext(34, ok({ reviewID: 12, reply: target, parentReply: null }));
            const replies = serveReplies([reply({ id: 30, content: 'An older reply' }), target]);
            renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            const shown = await screen.findByText('The reply from the notification');
            expect(screen.getByRole('button', { name: 'Hide replies' })).toBeInTheDocument();
            expect(await screen.findByText('An older reply')).toBeInTheDocument();
            expect(replies.last.url.searchParams.get('reviewId')).toBe('12');
            await waitFor(() => expect(Element.prototype.scrollIntoView).toHaveBeenCalled());
            expect(vi.mocked(Element.prototype.scrollIntoView).mock.contexts.at(-1)).toContainElement(shown);
        });

        it('opens a layer 3 reply inside its expanded parent', async () => {
            const parent = reply({ id: 30, content: 'Parent reply', childCount: 1 });
            const target = reply({ id: 34, parentReplyID: 30, content: 'Child reply' });
            serveReview(ok(profileReview({ id: 12, replyCount: 2 })));
            serveContext(34, ok({ reviewID: 12, reply: target, parentReply: parent }));
            const replies = capture('get', 'ReviewReply', req => ok({
                items: req.url.searchParams.get('parentReplyId') === '30' ? [target] : [parent],
                hasMore: false,
            }));
            server.use(replies.handler);
            renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            expect(await screen.findByText('Parent reply')).toBeInTheDocument();
            expect(screen.getByText('Child reply')).toBeInTheDocument();
            // The thread and the parent reply are both open
            expect(screen.getAllByRole('button', { name: 'Hide replies' })).toHaveLength(2);
            await waitFor(() => expect(replies.calls.some(c => c.url.searchParams.get('parentReplyId') === '30')).toBe(true));
        });

        it('says when the reply is gone, and still shows the review', async () => {
            serveReview(ok(profileReview({ id: 12, title: 'Arrival' })));
            serveContext(34, apiError(404, 'Reply not found.'));
            renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            expect(await screen.findByText('The reply you were looking for was deleted or is no longer available.')).toBeInTheDocument();
            expect(screen.getByText('Arrival')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /Replies/ })).toBeInTheDocument();
        });

        it('treats a reply of another review as gone', async () => {
            serveReview(ok(profileReview({ id: 12 })));
            serveContext(34, ok({ reviewID: 99, reply: reply({ id: 34 }), parentReply: null }));
            renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            expect(await screen.findByText(/The reply you were looking for was deleted/)).toBeInTheDocument();
        });

        it('loads the review and the reply in parallel', async () => {
            const reviewResponse = deferred(() => ok(profileReview({ id: 12 })));
            serveReview(() => reviewResponse.promise);
            const context = serveContext(34, apiError(404, 'gone'));
            renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            await waitFor(() => expect(context.calls).toHaveLength(1));
            reviewResponse.release();
            await screen.findByText(/The reply you were looking for/);
        });
    });

    knownIssue('W12', 'opened without ?reply= it loads the review once', async () => {
        const single = serveReview(ok(profileReview({ id: 12, username: 'bob', isOwner: true })));
        const { user } = renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12' });
        await screen.findByRole('heading', { name: "bob's review" });

        // Any re-render (here a tooltip) runs the load effect again: its replyId dependency is NaN, and Preact compares with !==
        await user.hover(screen.getByRole('button', { name: 'Delete Review' }));
        await new Promise(resolve => setTimeout(resolve, 200));

        expect(single.calls).toHaveLength(1);
    });

    // Opened at a reply (as from a notification): without ?reply= the page reloads itself on every render (W12)
    describe('deleting', () => {
        function serveGoneReply() {
            serveContext(34, apiError(404, 'Reply not found.'));
        }

        it('goes to the author\'s reviews after deleting', async () => {
            stubConfirm(true);
            serveReview(ok(profileReview({ id: 12, username: 'a b', isOwner: true })));
            serveGoneReply();
            const remove = capture('delete', 'Review/12', noContent());
            server.use(remove.handler);
            const { user } = renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34', user: authUser({ username: 'a b' }) });

            await user.click(await screen.findByRole('button', { name: 'Delete Review' }));

            await waitFor(() => expect(currentLocation()).toBe('/profile/a%20b/reviews'));
            expect(remove.calls).toHaveLength(1);
        });

        it('stays when cancelled', async () => {
            stubConfirm(false);
            serveReview(ok(profileReview({ id: 12, username: 'bob', isOwner: true })));
            serveGoneReply();
            const remove = capture('delete', 'Review/12', noContent());
            server.use(remove.handler);
            const { user } = renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            await user.click(await screen.findByRole('button', { name: 'Delete Review' }));

            expect(currentLocation()).toBe('/review/12?reply=34');
            expect(remove.calls).toHaveLength(0);
            expect(screen.getByRole('heading', { name: "bob's review" })).toBeInTheDocument();
        });

        it('says why the delete failed and stays', async () => {
            stubConfirm(true);
            const alert = stubAlert();
            serveReview(ok(profileReview({ id: 12, username: 'bob', isOwner: true })));
            serveGoneReply();
            server.use(capture('delete', 'Review/12', apiError(500, 'Could not delete.')).handler);
            const { user } = renderWithApp(<ReviewPage />, { path: PATH, route: '/review/12?reply=34' });

            await user.click(await screen.findByRole('button', { name: 'Delete Review' }));

            await waitFor(() => expect(alert).toHaveBeenCalledWith('Could not delete.'));
            expect(currentLocation()).toBe('/review/12?reply=34');
            expect(screen.getByRole('heading', { name: "bob's review" })).toBeInTheDocument();
        });
    });
});
