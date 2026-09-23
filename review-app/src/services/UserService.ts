import { ReviewFilterParams } from "../types/types";

class UserService {
    private readonly baseUrl = 'https://localhost:7140/api/User';

    async searchUsers(query: string) {
        try {
            const response = await fetch(`${this.baseUrl}/search?q=${encodeURIComponent(query)}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                return { success: false, data: [] }
            }
        } catch (error) {
            console.error('SearchUsers error:', error);
            return { success: false, data: [] }
        }
    }

    async getUserProfile(username: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${encodeURIComponent(username)}`,this.getFetchOptions());

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

    async changeUsername(username: string) {
        try {
            const response = await fetch(`${this.baseUrl}/me`, this.getFetchOptions('PATCH', { username }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                return { success: false, message: await this.getErrorMessage(response, 'Something went wrong while changing username.') }
            }
        } catch (error) {
            console.error('ChangeUsername error:', error);
            return { success: false, message: 'Network error while changing username.' }
        }
    }

    async changeEmail(newEmail: string, currentPassword: string) {
        try {
            const response = await fetch(`${this.baseUrl}/me/email`, this.getFetchOptions('PUT', { newEmail, currentPassword }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                return { success: false, message: await this.getErrorMessage(response, 'Something went wrong while changing email.') }
            }
        } catch (error) {
            // Never log the request body here - it contains the password
            console.error('ChangeEmail error:', error);
            return { success: false, message: 'Network error while changing email.' }
        }
    }

    async changePassword(currentPassword: string, newPassword: string) {
        try {
            const response = await fetch(`${this.baseUrl}/me/password`, this.getFetchOptions('PUT', { currentPassword, newPassword }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message };
            } else {
                return { success: false, message: await this.getErrorMessage(response, 'Something went wrong while changing password.') }
            }
        } catch (error) {
            // Never log the request body here - it contains the passwords
            console.error('ChangePassword error:', error);
            return { success: false, message: 'Network error while changing password.' }
        }
    }

    async getUserCollections(username: string, sortBy: string = 'createdAt_desc') {
        try {
            const response = await fetch(`${this.baseUrl}/${encodeURIComponent(username)}/collections?sortBy=${encodeURIComponent(sortBy)}`, this.getFetchOptions());

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

            const response = await fetch(`${this.baseUrl}/${encodeURIComponent(username)}/reviews?${query.toString()}`, this.getFetchOptions());

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
            const response = await fetch(`${this.baseUrl}/${encodeURIComponent(username)}/followers`, this.getFetchOptions());

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
            const response = await fetch(`${this.baseUrl}/${encodeURIComponent(username)}/following`, this.getFetchOptions());

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

    // Our endpoints return { error }, DataAnnotations failures come back as ProblemDetails ({ errors: { Field: [messages] } })
    // and a revoked / expired token gives a 401 with an empty body
    private async getErrorMessage(response: Response, fallback: string): Promise<string> {
        if (response.status === 401)
            return 'Your session has expired. Please log in again.';

        const errorData = await response.json().catch(() => null);
        if (errorData?.error)
            return errorData.error;

        const firstValidationError = Object.values(errorData?.errors ?? {})[0];
        if (Array.isArray(firstValidationError) && firstValidationError.length > 0)
            return firstValidationError[0];

        return fallback;
    }

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