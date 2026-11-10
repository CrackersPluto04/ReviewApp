import { useState } from "preact/hooks";
import { ReplyDto } from "../types/types";
import { reviewReplyService } from "../services/ReviewReplyService";

const PAGE_SIZE = 10;

// Keeps replies ordered oldest first (IDs grow over time) and drops duplicates (a locally added reply can come back in a later page)
function mergeReplies(current: ReplyDto[], incoming: ReplyDto[]) {
    const byId = new Map<number, ReplyDto>();
    [...current, ...incoming].forEach(r => byId.set(r.id, r));
    return [...byId.values()].sort((a, b) => a.id - b.id);
}

/**
 * "Load more" list of the direct replies of a review or of a layer-2 reply. Counts and placeholders are updated
 * locally after a create or delete instead of refetching, hence the mutators.
 *
 * Paging uses the last ID received from the server, not the last ID in the list, so replies added locally
 * don't make the next page skip older ones.
 *
 * @param reviewId The review the thread belongs to.
 * @param parentReplyId Omit for the review's direct replies (layer 2); the layer-2 reply's ID for its children (layer 3).
 * @param initial Seeds the list, e.g. a reply opened from a notification; later pages are merged around it.
 * @returns The list state and its actions:
 * - `replies`: the loaded replies, oldest first.
 * - `hasMore`: whether the server has another page; `false` until the first `loadMore` finished.
 * - `loaded`: whether at least one page was fetched. A seeded list is not "loaded", so the caller knows to fetch.
 * - `loading`, `errorMessage`: the state of the request in flight or its failure message.
 * - `loadMore()`: fetches the next page; ignored while a request is running.
 * - `addReply(reply)`: inserts a reply the user just posted.
 * - `removeReply(id)`: drops a reply from the list.
 * - `markDeleted(id)`: turns a reply into a "deleted" placeholder, as the API shows them, so its children stay visible.
 */
export function useReplyList(reviewId: number, parentReplyId?: number, initial?: ReplyDto[]) {
    const [replies, setReplies] = useState<ReplyDto[]>(initial ?? []);
    const [cursor, setCursor] = useState<number | undefined>(undefined);
    const [hasMore, setHasMore] = useState(false);
    const [loaded, setLoaded] = useState(false);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    const loadMore = async () => {
        if (loading) return;
        setLoading(true);
        setErrorMessage('');

        const result = await reviewReplyService.getReplies(reviewId, parentReplyId, cursor, PAGE_SIZE);

        if (result.success) {
            const items: ReplyDto[] = result.data.items;
            setReplies(prev => mergeReplies(prev, items));
            if (items.length > 0) setCursor(items[items.length - 1].id);
            setHasMore(result.data.hasMore);
            setLoaded(true);
        } else
            setErrorMessage(result.message);

        setLoading(false);
    };

    const addReply = (reply: ReplyDto) => setReplies(prev => mergeReplies(prev, [reply]));

    const removeReply = (replyId: number) => setReplies(prev => prev.filter(r => r.id !== replyId));

    const markDeleted = (replyId: number) => setReplies(prev => prev.map(r => r.id === replyId
        ? { ...r, isDeleted: true, isOwner: false, content: null, username: null, profilePictureUrl: null }
        : r));

    return { replies, hasMore, loaded, loading, errorMessage, loadMore, addReply, removeReply, markDeleted };
}
