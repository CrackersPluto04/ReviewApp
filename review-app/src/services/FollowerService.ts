import { apiFetch } from "./apiClient";

/**
 * Wraps `api/Follower`. All calls need a login and act as the logged-in user.
 * Methods never throw; they resolve to `{ success, message }`, the message being the API's confirmation on
 * success and the text to show on failure.
 */
class FollowerService {
    private readonly baseUrl = 'https://localhost:7140/api/Follower';

    /**
     * Follows a user.
     * @param targetUserId The ID of the user to follow.
     * @returns `{ success, message }`.
     */
    async follow(targetUserId: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${targetUserId}/follow`, this.getFetchOptions('POST'));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while trying to follow.' }
            }
        } catch (error) {
            console.error('Follow error:', error);
            return { success: false, message: 'Network error while reaching trying to follow.' }
        }
    }

    /**
     * Stops following a user.
     * @param targetUserId The ID of the user to unfollow.
     * @returns `{ success, message }`.
     */
    async unfollow(targetUserId: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${targetUserId}/unfollow`, this.getFetchOptions('DELETE'));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while trying to unfollow.' }
            }
        } catch (error) {
            console.error('Follow error:', error);
            return { success: false, message: 'Network error while reaching trying to unfollow.' }
        }
    }

    /**
     * Removes someone from the caller's followers, the reverse of `unfollow`.
     * @param followerToRemoveId The ID of the follower to remove.
     * @returns `{ success, message }`.
     */
    async remove(followerToRemoveId: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${followerToRemoveId}/remove`, this.getFetchOptions('DELETE'));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while trying to remove follower.' }
            }
        } catch (error) {
            console.error('Follow error:', error);
            return { success: false, message: 'Network error while reaching trying to remove follower.' }
        }
    }

    private getFetchOptions(method: string = 'GET', body?: any): RequestInit {
        return {
            method,
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: body ? JSON.stringify(body) : undefined
        };
    }
}

export const followerService = new FollowerService();
