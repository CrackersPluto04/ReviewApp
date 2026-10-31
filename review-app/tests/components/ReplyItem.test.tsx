import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { ReplyItem } from '../../src/components/ReplyItem';
import { server } from '../support/server';
import { apiError, capture, deferred, noContent, ok } from '../support/api';
import { authUser, deletedReply, reply } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { stubAlert, stubConfirm, stubMatchMedia } from '../support/browser';

type Props = Parameters<typeof ReplyItem>[0];

function renderItem(props: Partial<Props> & { reply: Props['reply'] }) {
    const handlers = { onDeleted: vi.fn(), onEmptied: vi.fn(), onCountChange: vi.fn() };
    const result = renderWithApp(<ReplyItem reviewId={7} layer={2} canReply {...handlers} {...props} />, { user: authUser() });
    return { ...result, ...handlers };
}

function serveChildren(items: ReturnType<typeof reply>[], hasMore = false) {
    const endpoint = capture('get', 'ReviewReply', () => ok({ items, hasMore }));
    server.use(endpoint.handler);
    return endpoint;
}

describe('ReplyItem', () => {
    it('shows the author, date and text as plain text', async () => {
        const { user } = renderItem({ reply: reply({ username: 'carol', createdAt: '2026-03-11', content: '<b>not bold</b>' }) });

        expect(screen.getByText('carol')).toBeInTheDocument();
        expect(screen.getByText('2026-03-11')).toBeInTheDocument();
        expect(screen.getByText('<b>not bold</b>')).toBeInTheDocument();

        await user.click(screen.getByText('carol'));
        expect(currentLocation()).toBe('/profile/carol');
    });

    it('opens the author from the avatar too', async () => {
        const { user } = renderItem({ reply: reply({ username: 'c c', profilePictureUrl: 'https://img.example/c.png' }) });

        await user.click(screen.getByRole('img', { name: 'c c' }));

        expect(currentLocation()).toBe('/profile/c%20c');
    });

    it('shows a deleted reply as a placeholder without author or actions', () => {
        renderItem({ reply: deletedReply({ isOwner: false }) });

        expect(screen.getByText('This reply was deleted')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Delete reply' })).not.toBeInTheDocument();
    });

    describe('children (layer 3)', () => {
        it('loads them when opened', async () => {
            const children = serveChildren([reply({ content: 'A child' })]);
            const { user } = renderItem({ reply: reply({ id: 30, childCount: 1 }) });

            await user.click(screen.getByRole('button', { name: 'View 1 reply' }));

            expect(await screen.findByText('A child')).toBeInTheDocument();
            expect(children.last.url.searchParams.get('parentReplyId')).toBe('30');
            await user.click(screen.getByRole('button', { name: 'Hide replies' }));
            await user.click(screen.getByRole('button', { name: 'View 1 reply' }));
            expect(children.calls).toHaveLength(1);
        });

        it('pluralizes and loads more', async () => {
            const children = capture('get', 'ReviewReply', req => req.url.searchParams.has('afterId')
                ? ok({ items: [reply({ id: 3, content: 'Third child' })], hasMore: false })
                : ok({ items: [reply({ id: 1 }), reply({ id: 2 })], hasMore: true }));
            server.use(children.handler);
            const { user } = renderItem({ reply: reply({ id: 30, childCount: 3 }) });

            await user.click(screen.getByRole('button', { name: 'View 3 replies' }));
            await user.click(await screen.findByRole('button', { name: 'Load more replies' }));

            expect(await screen.findByText('Third child')).toBeInTheDocument();
        });

        it('shows their loading error', async () => {
            server.use(capture('get', 'ReviewReply', apiError(500, 'Could not load replies.')).handler);
            const { user } = renderItem({ reply: reply({ childCount: 1 }) });

            await user.click(screen.getByRole('button', { name: 'View 1 reply' }));

            expect(await screen.findByText('Could not load replies.')).toBeInTheDocument();
        });

        it('cannot be replied to or opened further', () => {
            renderItem({ reply: reply({ childCount: 0 }), layer: 3, canReply: false });

            expect(screen.queryByRole('button', { name: 'Reply' })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /View/ })).not.toBeInTheDocument();
        });
    });

    describe('replying', () => {
        it('opens a form addressed to the author and adds the reply as a child', async () => {
            serveChildren([]);
            const create = capture('post', 'ReviewReply', ok(reply({ id: 60, parentReplyID: 30, content: 'Me too' })));
            server.use(create.handler);
            const { user, onCountChange } = renderItem({ reply: reply({ id: 30, username: 'carol' }) });

            await user.click(screen.getByRole('button', { name: 'Reply' }));
            const box = screen.getByPlaceholderText('Reply to carol...');
            await user.type(box, 'Me too');
            await user.click(screen.getAllByRole('button', { name: 'Reply' })[1]);

            expect(await screen.findByText('Me too', { selector: 'p' })).toBeInTheDocument();
            expect(create.last.body).toEqual({ reviewID: 7, parentReplyID: 30, content: 'Me too' });
            expect(screen.queryByPlaceholderText('Reply to carol...')).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Hide replies' })).toBeInTheDocument();
            expect(onCountChange).toHaveBeenCalledWith(1);
        });

        it('closes the form on cancel', async () => {
            const { user } = renderItem({ reply: reply({ username: 'carol' }) });

            await user.click(screen.getByRole('button', { name: 'Reply' }));
            await user.click(screen.getByRole('button', { name: 'Cancel' }));

            expect(screen.queryByPlaceholderText('Reply to carol...')).not.toBeInTheDocument();
        });

        it('has no reply button when replying isn\'t allowed', () => {
            renderItem({ reply: reply(), canReply: false });
            expect(screen.queryByRole('button', { name: 'Reply' })).not.toBeInTheDocument();
        });
    });

    describe('deleting', () => {
        it('asks first, then reports a reply without children for removal', async () => {
            const confirm = stubConfirm(true);
            const remove = capture('delete', 'ReviewReply/30', noContent());
            server.use(remove.handler);
            const { user, onDeleted } = renderItem({ reply: reply({ id: 30, isOwner: true }) });

            await user.click(screen.getByRole('button', { name: 'Delete reply' }));

            await waitFor(() => expect(onDeleted).toHaveBeenCalledWith(30, false));
            expect(confirm).toHaveBeenCalledWith('Are you sure you want to delete this reply? This cannot be undone!');
            expect(remove.calls).toHaveLength(1);
        });

        it('keeps a reply with children as a placeholder', async () => {
            stubConfirm(true);
            server.use(capture('delete', 'ReviewReply/30', noContent()).handler);
            const { user, onDeleted } = renderItem({ reply: reply({ id: 30, isOwner: true, childCount: 2 }) });

            await user.click(screen.getByRole('button', { name: 'Delete reply' }));

            await waitFor(() => expect(onDeleted).toHaveBeenCalledWith(30, true));
        });

        it('always removes a layer 3 reply', async () => {
            stubConfirm(true);
            server.use(capture('delete', 'ReviewReply/31', noContent()).handler);
            const { user, onDeleted } = renderItem({ reply: reply({ id: 31, isOwner: true, childCount: 0 }), layer: 3, canReply: false });

            await user.click(screen.getByRole('button', { name: 'Delete reply' }));

            await waitFor(() => expect(onDeleted).toHaveBeenCalledWith(31, false));
        });

        it('does nothing when cancelled', async () => {
            stubConfirm(false);
            const remove = capture('delete', 'ReviewReply/30', noContent());
            server.use(remove.handler);
            const { user, onDeleted } = renderItem({ reply: reply({ id: 30, isOwner: true }) });

            await user.click(screen.getByRole('button', { name: 'Delete reply' }));

            expect(remove.calls).toHaveLength(0);
            expect(onDeleted).not.toHaveBeenCalled();
        });

        it('disables the button while deleting and alerts on failure', async () => {
            stubConfirm(true);
            const alert = stubAlert();
            const response = deferred(() => apiError(403, 'Not your reply.'));
            server.use(capture('delete', 'ReviewReply/30', () => response.promise).handler);
            const { user, onDeleted } = renderItem({ reply: reply({ id: 30, isOwner: true }) });

            await user.click(screen.getByRole('button', { name: 'Delete reply' }));
            await waitFor(() => expect(screen.getByRole('button', { name: 'Delete reply' })).toBeDisabled());
            response.release();

            await waitFor(() => expect(alert).toHaveBeenCalledWith('Not your reply.'));
            expect(screen.getByRole('button', { name: 'Delete reply' })).toBeEnabled();
            expect(onDeleted).not.toHaveBeenCalled();
        });

        it('a placeholder disappears when its last child is deleted', async () => {
            stubConfirm(true);
            serveChildren([reply({ id: 41, isOwner: true, content: 'Last child' })]);
            server.use(capture('delete', 'ReviewReply/41', noContent()).handler);
            const { user, onEmptied, onCountChange } = renderItem({ reply: deletedReply({ id: 40, childCount: 1 }) });

            await user.click(screen.getByRole('button', { name: 'View 1 reply' }));
            await screen.findByText('Last child');
            await user.click(screen.getByRole('button', { name: 'Delete reply' }));

            await waitFor(() => expect(onEmptied).toHaveBeenCalledWith(40));
            expect(onCountChange).toHaveBeenCalledWith(-1);
        });

        it('a living reply stays when its last child is deleted', async () => {
            stubConfirm(true);
            serveChildren([reply({ id: 41, isOwner: true, content: 'Only child' })]);
            server.use(capture('delete', 'ReviewReply/41', noContent()).handler);
            const { user, onEmptied } = renderItem({ reply: reply({ id: 40, childCount: 1, content: 'Parent' }) });

            await user.click(screen.getByRole('button', { name: 'View 1 reply' }));
            await screen.findByText('Only child');
            await user.click(screen.getAllByRole('button', { name: 'Delete reply' }).slice(-1)[0]);

            await waitFor(() => expect(screen.queryByText('Only child')).not.toBeInTheDocument());
            expect(onEmptied).not.toHaveBeenCalled();
            expect(screen.queryByRole('button', { name: /View|Hide replies/ })).not.toBeInTheDocument();
        });
    });

    describe('focus from a notification', () => {
        it('scrolls a highlighted reply into view, smoothly', () => {
            renderItem({ reply: reply({ content: 'Look here' }), highlighted: true });

            expect(Element.prototype.scrollIntoView).toHaveBeenLastCalledWith({ behavior: 'smooth', block: 'center' });
        });

        it('jumps without animation for reduced motion', () => {
            stubMatchMedia(query => query.includes('prefers-reduced-motion'));
            renderItem({ reply: reply(), highlighted: true });

            expect(Element.prototype.scrollIntoView).toHaveBeenLastCalledWith({ behavior: 'auto', block: 'center' });
        });

        it('opens the children at a focused child', async () => {
            const child = reply({ id: 34, content: 'Focused child' });
            const children = serveChildren([child, reply({ id: 35, content: 'Its sibling' })]);
            renderItem({ reply: reply({ id: 30, childCount: 2 }), focusChild: child });

            expect(screen.getByText('Focused child')).toBeInTheDocument();
            expect(await screen.findByText('Its sibling')).toBeInTheDocument();
            expect(children.last.url.searchParams.get('parentReplyId')).toBe('30');
        });
    });
});
