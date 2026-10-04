import { apiFetch } from "./apiClient";
import { CreateReplyDto } from "../types/types";

class ReviewReplyService {
    private readonly baseUrl = 'https://localhost:7140/api/ReviewReply';

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
