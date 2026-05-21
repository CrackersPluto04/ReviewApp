import { ReviewFilterParams } from "../types/types";

class UserService {
    private readonly baseUrl = 'https://localhost:7140/api/User';

    async getUserProfile(username: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${username}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user profile infos.' }
            }
        } catch (error) {
            console.error('GetUserProfile error:', error);
            return { success: false, message: 'Network error while reaching user profile infos.' }
        }
    }

    async updateMyProfile(bio?: string, profilePictureUrl?: string) {
        try {
            const response = await fetch(`${this.baseUrl}/me`, this.getFetchOptions('PATCH', { bio, profilePictureUrl }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while updating user profile.' }
            }
        } catch (error) {
            console.error('UpdateMyProfile error:', error);
            return { success: false, message: 'Network error while updating user profile.' }
        }
    }

    async getUserCollections(username: string, sortBy: string = 'createdAt_desc') {
        try {
            const response = await fetch(`${this.baseUrl}/${username}/collections?sortBy=${sortBy}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user collections.' }
            }
        } catch (error) {
            console.error('GetUserCollections error:', error);
            return { success: false, message: 'Network error while reaching user collections.' }
        }
    }

    async getUserReviews(username: string, params: ReviewFilterParams) {
        try {
            const query = new URLSearchParams();

            query.append('page', params.page.toString());
            if (params.sortBy) query.append('sortBy', params.sortBy);
            if (params.minScore !== undefined) query.append('minScore', params.minScore.toString());
            if (params.maxScore !== undefined) query.append('maxScore', params.maxScore.toString());
            if (params.hasWrittenText) query.append('hasWrittenText', 'true');

            const response = await fetch(`${this.baseUrl}/${username}/reviews?${query.toString()}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user reviews.' }
            }
        } catch (error) {
            console.error('GetUserReviews error:', error);
            return { success: false, message: 'Network error while reaching user reviews.' }
        }
    }

    async getUserFollowers(username: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${username}/followers`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user followers.' }
            }
        } catch (error) {
            console.error('GetUserFollowers error:', error);
            return { success: false, message: 'Network error while reaching user followers.' }
        }
    }

    async getUserFollowing(username: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${username}/following`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user following.' }
            }
        } catch (error) {
            console.error('GetUserFollowing error:', error);
            return { success: false, message: 'Network error while reaching user following.' }
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

export const userService = new UserService();