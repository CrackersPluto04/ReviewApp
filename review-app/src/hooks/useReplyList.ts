import { useState } from "preact/hooks";
import { ReplyDto } from "../types/types";
import { reviewReplyService } from "../services/ReviewReplyService";

const PAGE_SIZE = 10;

// Keeps replies ordered oldest first and drops duplicates (a locally added reply can come back in a later page)
function mergeReplies(current: ReplyDto[], incoming: ReplyDto[]) {
    const byId = new Map<number, ReplyDto>();
    [...current, ...incoming].forEach(r => byId.set(r.id, r));
    return [...byId.values()].sort((a, b) => a.id - b.id);
}

/**
 * "Load more" list of the direct replies of a review (parentReplyId undefined) or of a reply.
 * Paging uses the last ID received from the server, not the last ID in the list,
 * so replies added locally don't make the next page skip older ones.
 */
export function useReplyList(reviewId: number, parentReplyId?: number) {
    const [replies, setReplies] = useState<ReplyDto[]>([]);
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

    // Turns a reply into a "deleted" placeholder, its children stay visible
    const markDeleted = (replyId: number) => setReplies(prev => prev.map(r => r.id === replyId
        ? { ...r, isDeleted: true, isOwner: false, content: null, username: null, profilePictureUrl: null }
        : r));

    return { replies, hasMore, loaded, loading, errorMessage, loadMore, addReply, removeReply, markDeleted };
}
