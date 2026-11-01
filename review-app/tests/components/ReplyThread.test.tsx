import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { ReplyThread } from '../../src/components/ReplyThread';
import { ReviewCard } from '../../src/components/ReviewCard';
import { server } from '../support/server';
import { apiError, capture, noContent, ok } from '../support/api';
import { authUser, deletedReply, profileReview, reply } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveReplies } from '../support/handlers';
import { stubConfirm } from '../support/browser';

function renderThread(props: Partial<Parameters<typeof ReplyThread>[0]> = {}, user: ReturnType<typeof authUser> | null = authUser()) {
    const onCountChange = vi.fn();
    const result = renderWithApp(
        <ReplyThread reviewId={7} replyCount={2} replyable onCountChange={onCountChange} {...props} />,
        { user },
    );
    return { ...result, onCountChange };
}

describe('ReplyThread', () => {
    it('loads nothing until opened', async () => {
        const replies = serveReplies([reply()]);
        renderThread();

        expect(screen.getByRole('button', { name: 'Replies (2)' })).toBeInTheDocument();
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(replies.calls).toHaveLength(0);
    });

    it('loads the replies once when opened and keeps them when closed', async () => {
        const replies = serveReplies([reply({ content: 'First!' })]);
        const { user } = renderThread();

        await user.click(screen.getByRole('button', { name: 'Replies (2)' }));
        expect(await screen.findByText('First!')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Hide replies' }));
        await user.click(screen.getByRole('button', { name: 'Replies (2)' }));

        expect(replies.calls).toHaveLength(1);
        expect(replies.last.url.searchParams.has('parentReplyId')).toBe(false);
    });

    it('says when there are no replies yet', async () => {
        serveReplies([]);
        const { user } = renderThread({ replyCount: 0 });

        await user.click(screen.getByRole('button', { name: 'Replies (0)' }));

        expect(await screen.findByText('No replies yet.')).toBeInTheDocument();
    });

    it('loads more replies after the last one', async () => {
        const replies = capture('get', 'ReviewReply', req => req.url.searchParams.has('afterId')
            ? ok({ items: [reply({ id: 3, content: 'Third' })], hasMore: false })
            : ok({ items: [reply({ id: 1, content: 'First' }), reply({ id: 2, content: 'Second' })], hasMore: true }));
        server.use(replies.handler);
        const { user } = renderThread({ replyCount: 3 });
        await user.click(screen.getByRole('button', { name: 'Replies (3)' }));
        await screen.findByText('Second');

        await user.click(screen.getByRole('button', { name: 'Load more replies' }));

        expect(await screen.findByText('Third')).toBeInTheDocument();
        expect(replies.last.url.searchParams.get('afterId')).toBe('2');
        expect(screen.queryByRole('button', { name: 'Load more replies' })).not.toBeInTheDocument();
    });

    it('shows the loading error', async () => {
        server.use(capture('get', 'ReviewReply', apiError(404, 'Review not found.')).handler);
        const { user } = renderThread();

        await user.click(screen.getByRole('button', { name: 'Replies (2)' }));

        expect(await screen.findByText('Review not found.')).toBeInTheDocument();
        expect(screen.queryByText('No replies yet.')).not.toBeInTheDocument();
    });

    it('asks a visitor to log in to reply', async () => {
        serveReplies([]);
        const { user } = renderThread({}, null);
        await user.click(await screen.findByRole('button', { name: 'Replies (2)' }));

        expect(await screen.findByText('Log in to join the conversation.')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Log In or Register' }));
        expect(currentLocation()).toBe('/login');
    });

    it('explains that private reviews can\'t be replied to', async () => {
        serveReplies([]);
        const { user } = renderThread({ replyable: false });
        await user.click(screen.getByRole('button', { name: 'Replies (2)' }));

        expect(await screen.findByText("Private reviews can't be replied to.")).toBeInTheDocument();
        expect(screen.queryByPlaceholderText('Write a reply...')).not.toBeInTheDocument();
    });

    it('adds a posted reply to the list and to the count', async () => {
        serveReplies([]);
        server.use(capture('post', 'ReviewReply', ok(reply({ id: 9, content: 'My reply', isOwner: true }))).handler);
        const { user, onCountChange } = renderThread({ replyCount: 0 });
        await user.click(screen.getByRole('button', { name: 'Replies (0)' }));
        await screen.findByText('No replies yet.');

        await user.type(await screen.findByPlaceholderText('Write a reply...'), 'My reply');
        await user.click(screen.getByRole('button', { name: 'Reply' }));

        // The reply's own text, not the field's
        expect(await screen.findByText('My reply', { selector: 'p' })).toBeInTheDocument();
        expect(onCountChange).toHaveBeenCalledWith(1);
        expect(screen.queryByText('No replies yet.')).not.toBeInTheDocument();
    });

    it('removes a deleted reply without children and keeps one with children as a placeholder', async () => {
        stubConfirm(true);
        serveReplies([
            reply({ id: 1, content: 'Lonely reply', isOwner: true }),
            reply({ id: 2, content: 'Popular reply', isOwner: true, childCount: 2 }),
        ]);
        server.use(capture('delete', 'ReviewReply/:id', noContent()).handler);
        const { user, onCountChange } = renderThread({ replyCount: 4 });
        await user.click(screen.getByRole('button', { name: 'Replies (4)' }));
        await screen.findByText('Lonely reply');

        const [first, second] = screen.getAllByRole('button', { name: 'Delete reply' });
        await user.click(first);
        await waitFor(() => expect(screen.queryByText('Lonely reply')).not.toBeInTheDocument());

        await user.click(second);
        expect(await screen.findByText('This reply was deleted')).toBeInTheDocument();
        expect(screen.queryByText('Popular reply')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'View 2 replies' })).toBeInTheDocument();
        expect(onCountChange.mock.calls).toEqual([[-1], [-1]]);
    });

    it('keeps the review card\'s count in sync', async () => {
        serveReplies([]);
        server.use(capture('post', 'ReviewReply', ok(reply({ id: 9, content: 'Counted' }))).handler);
        const { user } = renderWithApp(<ReviewCard rev={profileReview({ id: 7, replyCount: 1 })} mode="profile" />, { user: authUser() });

        await user.click(screen.getByRole('button', { name: 'Replies (1)' }));
        await user.type(await screen.findByPlaceholderText('Write a reply...'), 'Counted');
        await user.click(screen.getByRole('button', { name: 'Reply' }));
        await screen.findByText('Counted', { selector: 'p' });
        await user.click(screen.getByRole('button', { name: 'Hide replies' }));

        expect(screen.getByRole('button', { name: 'Replies (2)' })).toBeInTheDocument();
    });

    it('shows a deleted placeholder from the server', async () => {
        serveReplies([deletedReply({ id: 4, childCount: 1 })]);
        const { user } = renderThread({ replyCount: 1 });

        await user.click(screen.getByRole('button', { name: 'Replies (1)' }));

        const placeholder = await screen.findByText('This reply was deleted');
        expect(within(placeholder.closest('.MuiBox-root')!.parentElement!).queryByRole('button', { name: 'Delete reply' })).not.toBeInTheDocument();
    });
});
