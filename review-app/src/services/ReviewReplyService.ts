import { apiFetch } from "./apiClient";
import { CreateReplyDto, ReplyContextDto } from "../types/types";

/**
 * Wraps `api/ReviewReply`. Threads are at most three layers deep: review, reply, reply to a reply. A reply has no
 * visibility of its own; it follows its review's, so a hidden review answers 404 for all of its replies.
 * Reading is open to anonymous visitors; posting and deleting need a login.
 * Methods never throw; they resolve to `{ success, data }` or `{ success: false, message }`.
 */
class ReviewReplyService {
    private readonly baseUrl = 'https://localhost:7140/api/ReviewReply';

    /**
     * Loads one page of a thread with keyset paging ("load more"), so replies posted meanwhile don't shift the pages.
     * @param reviewId The review whose thread is read.
     * @param parentReplyId Omitted for the review's direct replies, or the layer-2 reply whose children are wanted.
     * @param afterId The ID of the last reply of the previous page.
     * @param pageSize How many replies to load.
     * @returns `{ success: true, data }` with the page, or `{ success: false, message }`.
     */
    async getReplies(reviewId: number, parentReplyId?: number, afterId?: number, pageSize: number = 10) {
        try {
            const query = new URLSearchParams();

            query.append('reviewId', reviewId.toString());
            if (parentReplyId !== undefined) query.append('parentReplyId', parentReplyId.toString());
            if (afterId !== undefined) query.append('afterId', afterId.toString());
            query.append('pageSize', pageSize.toString());

            const response = await apiFetch(`${this.baseUrl}?${query.toString()}`, {
                credentials: 'include'
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting the replies.' }
            }
        } catch (error) {
            console.error('GetReplies error:', error);
            return { success: false, message: 'Network error while reaching replies.' }
        }
    }

    /**
     * Loads one reply together with its layer-2 parent, to open a thread at a specific reply (the target of
     * reply notifications).
     * @param id The reply's ID.
     * @returns `{ success: true, data }`, or `{ success: false, message }` when the reply is deleted, doesn't
     * exist or its review is hidden from the viewer (all 404, so callers cannot tell them apart).
     */
    async getReplyContext(id: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${id}`, { credentials: 'include' });

            if (response.ok) {
                const data: ReplyContextDto = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json().catch(() => null);
                return { success: false, message: errorData?.error || 'Something went wrong while getting the reply.' }
            }
        } catch (error) {
            console.error('GetReplyContext error:', error);
            return { success: false, message: 'Network error while reaching the reply.' }
        }
    }

    /**
     * Posts a reply to a review or to a layer-2 reply. Notifies the person replied to.
     * @param data The review, the optional parent reply and the text (see `isValidReply` for the limits).
     * @returns `{ success: true, data }` with the created reply, or a failure (e.g. replying to a layer-3
     * reply or to a private review).
     */
    async createReply(data: CreateReplyDto) {
        try {
            const response = await apiFetch(this.baseUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify(data)
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while posting reply. Please try again.' }
            }
        } catch (error) {
            console.error('Create reply error:', error);
            return { success: false, message: 'Network error while reaching replies.' }
        }
    }

    /**
     * Deletes the caller's own reply. A soft delete on the API: the thread shows a placeholder while the reply
     * still has answers.
     * @param id The reply's ID.
     * @returns `{ success: true, data: null }`, or `{ success: false, message }`.
     */
    async deleteReply(id: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${id}`, {
                method: 'DELETE',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include'
            });

            if (response.ok) {
                return { success: true, data: null };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while deleting reply. Please try again.' }
            }
        } catch (error) {
            console.error('Delete reply error:', error);
            return { success: false, message: 'Network error while reaching replies.' }
        }
    }
}

export const reviewReplyService = new ReviewReplyService();
