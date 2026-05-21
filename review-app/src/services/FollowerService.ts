class FollowerService {
    private readonly baseUrl = 'https://localhost:7140/api/Follower';

    async follow(targetUserId: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${targetUserId}/follow`, this.getFetchOptions('POST'));

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

    async unfollow(targetUserId: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${targetUserId}/unfollow`, this.getFetchOptions('DELETE'));

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

    async remove(followerToRemoveId: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${followerToRemoveId}/remove`, this.getFetchOptions('DELETE'));

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

    /* Helper methods */

    // Create fetch options for different HTTP methods and request bodies
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