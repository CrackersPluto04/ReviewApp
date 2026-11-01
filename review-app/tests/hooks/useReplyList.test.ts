import { describe, expect, it } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { useReplyList } from '../../src/hooks/useReplyList';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { reply } from '../support/builders';
import { knownIssue } from '../support/known-issue';

const page = (items: ReturnType<typeof reply>[], hasMore = false) => ({ items, hasMore });

function serveReplies(respond: Parameters<typeof capture>[2]) {
    const replies = capture('get', 'ReviewReply', respond);
    server.use(replies.handler);
    return replies;
}

describe('useReplyList', () => {
    it('starts empty and not loaded, or with the seed', () => {
        const empty = renderHook(() => useReplyList(1));
        expect(empty.result.current).toMatchObject({ replies: [], hasMore: false, loaded: false, loading: false, errorMessage: '' });

        const seed = reply({ id: 50 });
        const seeded = renderHook(() => useReplyList(1, undefined, [seed]));
        expect(seeded.result.current.replies).toEqual([seed]);
        expect(seeded.result.current.loaded).toBe(false);
    });

    it('loads the first page of a review\'s replies', async () => {
        const items = [reply({ id: 1 }), reply({ id: 2 })];
        const replies = serveReplies(ok(page(items, true)));
        const { result } = renderHook(() => useReplyList(7));

        await act(() => result.current.loadMore());

        expect(result.current).toMatchObject({ replies: items, hasMore: true, loaded: true, loading: false });
        const query = replies.last.url.searchParams;
        expect(query.get('reviewId')).toBe('7');
        expect(query.has('parentReplyId')).toBe(false);
        expect(query.has('afterId')).toBe(false);
        expect(query.get('pageSize')).toBe('10');
    });

    it('loads the children of a reply', async () => {
        const replies = serveReplies(ok(page([])));
        const { result } = renderHook(() => useReplyList(7, 3));

        await act(() => result.current.loadMore());

        expect(replies.last.url.searchParams.get('parentReplyId')).toBe('3');
        expect(result.current.loaded).toBe(true);
    });

    it('pages with the last id the server sent, not the last id in the list', async () => {
        const replies = serveReplies(req => req.url.searchParams.has('afterId')
            ? ok(page([reply({ id: 3 })]))
            : ok(page([reply({ id: 1 }), reply({ id: 2 })], true)));
        const { result } = renderHook(() => useReplyList(7));

        await act(() => result.current.loadMore());
        // Added locally, newer than anything loaded
        act(() => result.current.addReply(reply({ id: 99 })));
        await act(() => result.current.loadMore());

        expect(replies.calls[1].url.searchParams.get('afterId')).toBe('2');
        expect(result.current.replies.map(r => r.id)).toEqual([1, 2, 3, 99]);
        expect(result.current.hasMore).toBe(false);
    });

    it('keeps the cursor on an empty page', async () => {
        const replies = serveReplies(req => req.url.searchParams.get('afterId') === null
            ? ok(page([reply({ id: 4 })], true))
            : ok(page([], true)));
        const { result } = renderHook(() => useReplyList(7));

        await act(() => result.current.loadMore());
        await act(() => result.current.loadMore());
        await act(() => result.current.loadMore());

        expect(replies.calls.map(c => c.url.searchParams.get('afterId'))).toEqual([null, '4', '4']);
    });

    it('merges pages around the seed without duplicates, oldest first', async () => {
        serveReplies(ok(page([reply({ id: 10 }), reply({ id: 20, content: 'from server' }), reply({ id: 30 })])));
        const { result } = renderHook(() => useReplyList(7, undefined, [reply({ id: 20, content: 'seed' })]));

        await act(() => result.current.loadMore());

        expect(result.current.replies.map(r => r.id)).toEqual([10, 20, 30]);
        // The server's version wins
        expect(result.current.replies[1].content).toBe('from server');
    });

    it('shows the error and keeps the list on failure, then retries', async () => {
        let fail = true;
        serveReplies(() => fail ? apiError(500, 'Replies are down') : ok(page([reply({ id: 1 })])));
        const { result } = renderHook(() => useReplyList(7, undefined, [reply({ id: 5 })]));

        await act(() => result.current.loadMore());
        expect(result.current).toMatchObject({ errorMessage: 'Replies are down', loaded: false, loading: false });
        expect(result.current.replies).toHaveLength(1);

        fail = false;
        await act(() => result.current.loadMore());
        expect(result.current.errorMessage).toBe('');
        expect(result.current.replies.map(r => r.id)).toEqual([1, 5]);
    });

    it('is loading while the request runs', async () => {
        const response = deferred(() => HttpResponse.json(page([])));
        serveReplies(() => response.promise);
        const { result } = renderHook(() => useReplyList(7));

        let load!: Promise<void>;
        act(() => { load = result.current.loadMore(); });
        await waitFor(() => expect(result.current.loading).toBe(true));

        response.release();
        await act(() => load);
        expect(result.current.loading).toBe(false);
    });

    it('ignores a second call while the first one runs (after a render)', async () => {
        const response = deferred(() => HttpResponse.json(page([reply({ id: 1 })])));
        const replies = serveReplies(() => response.promise);
        const { result } = renderHook(() => useReplyList(7));

        let first!: Promise<void>;
        act(() => { first = result.current.loadMore(); });
        await waitFor(() => expect(result.current.loading).toBe(true));
        await act(() => result.current.loadMore());

        response.release();
        await act(() => first);
        expect(replies.calls).toHaveLength(1);
    });

    knownIssue('W1', 'two calls in the same render start only one request', async () => {
        const replies = serveReplies(ok(page([reply({ id: 1 })])));
        const { result } = renderHook(() => useReplyList(7));

        // e.g. a double click: both calls see the same `loading === false` from the closure
        await act(async () => { await Promise.all([result.current.loadMore(), result.current.loadMore()]); });

        expect(replies.calls).toHaveLength(1);
    });

    knownIssue('W1', 'a malformed response body does not leave the list loading forever', async () => {
        serveReplies(() => HttpResponse.json({ unexpected: true }));
        const { result } = renderHook(() => useReplyList(7));

        await act(() => result.current.loadMore().catch(() => undefined));

        expect(result.current.loading).toBe(false);
    });

    knownIssue('W1', 'switching to another review starts a fresh list', async () => {
        serveReplies(req => ok(page([reply({ id: Number(req.url.searchParams.get('reviewId')) * 100 })])));
        const { result, rerender } = renderHook(({ reviewId }) => useReplyList(reviewId), { initialProps: { reviewId: 1 } });

        await act(() => result.current.loadMore());
        rerender({ reviewId: 2 });

        expect(result.current.replies).toEqual([]);
        expect(result.current.loaded).toBe(false);
    });

    it('adds a reply in id order, without duplicating it', () => {
        const { result } = renderHook(() => useReplyList(7, undefined, [reply({ id: 1 }), reply({ id: 3 })]));

        act(() => result.current.addReply(reply({ id: 2 })));
        act(() => result.current.addReply(reply({ id: 2, content: 'edited' })));

        expect(result.current.replies.map(r => r.id)).toEqual([1, 2, 3]);
        expect(result.current.replies[1].content).toBe('edited');
    });

    it('removes a reply', () => {
        const { result } = renderHook(() => useReplyList(7, undefined, [reply({ id: 1 }), reply({ id: 2 })]));

        act(() => result.current.removeReply(1));

        expect(result.current.replies.map(r => r.id)).toEqual([2]);
    });

    it('turns a reply into a deleted placeholder that keeps its children count', () => {
        const original = reply({ id: 1, content: 'Hi', username: 'carol', profilePictureUrl: 'x.png', isOwner: true, childCount: 2 });
        const { result } = renderHook(() => useReplyList(7, undefined, [original]));

        act(() => result.current.markDeleted(1));

        expect(result.current.replies[0]).toEqual({
            ...original, isDeleted: true, isOwner: false, content: null, username: null, profilePictureUrl: null,
        });
    });
});
