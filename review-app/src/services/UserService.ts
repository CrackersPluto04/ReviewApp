import { apiFetch } from "./apiClient";
import { ReviewFilterParams } from "../types/types";

/**
 * Wraps `api/User`: profiles and the content shown on them (reviews, collections, follower lists), and the
 * logged-in user's account changes (`/me`). Reading is open to anonymous visitors, with visibility decided per
 * viewer; the account changes need a login.
 * Methods never throw; they resolve to `{ success, data }`, `{ success, message }` or `{ success: false, message }`.
 */
class UserService {
    private readonly baseUrl = 'https://localhost:7140/api/User';

    /**
     * Searches users by username for the user search.
     * @param query The search text.
     * @returns `{ success: true, data }` with the matching users. On any failure `{ success: false, data: [] }`
     * with an empty list and no message, so a search box can simply show "no results".
     */
    async searchUsers(query: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/search?q=${encodeURIComponent(query)}`, this.getFetchOptions());

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

    /**
     * Loads a user's public profile.
     * @param username The profile's username.
     * @returns `{ success: true, data }` with the profile, or `{ success: false, message }` (e.g. unknown user).
     */
    async getUserProfile(username: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}`,this.getFetchOptions());

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

    /**
     * Updates the logged-in user's bio and/or profile picture. `undefined` fields are left out of the request
     * (`JSON.stringify` drops them), so they stay unchanged.
     * @param bio The new bio.
     * @param profilePictureUrl The new picture URL.
     * @returns `{ success, message }`.
     */
    async updateMyProfile(bio?: string, profilePictureUrl?: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/me`, this.getFetchOptions('PATCH', { bio, profilePictureUrl }));

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

    /**
     * Renames the logged-in user. Usernames are part of profile URLs, so the API applies the username rules
     * (see `usernameError`).
     * @param username The new username.
     * @returns `{ success, message }`.
     */
    async changeUsername(username: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/me`, this.getFetchOptions('PATCH', { username }));

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

    /**
     * Requests an email change. Nothing changes yet: the API stores it as pending and emails a confirmation link to
     * the new address (see `SecurityService.confirmEmailChange`). Needs a verified current email.
     * @param newEmail The new address.
     * @param currentPassword The account password.
     * @returns `{ success, message }`.
     */
    async changeEmail(newEmail: string, currentPassword: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/me/email`, this.getFetchOptions('PUT', { newEmail, currentPassword }));

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

    /**
     * Changes the password. Revokes every other session; the response re-issues the cookie for this browser, so
     * the user stays logged in here.
     * @param currentPassword The current password.
     * @param newPassword The new password, within the limits of `utils/validation`.
     * @returns `{ success, message }`.
     */
    async changePassword(currentPassword: string, newPassword: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/me/password`, this.getFetchOptions('PUT', { currentPassword, newPassword }));

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

    /**
     * Lists a user's collections that the viewer may see.
     * @param username The profile's username.
     * @param sortBy The sort key, e.g. `createdAt_desc`.
     * @returns `{ success: true, data }` with the collections, or `{ success: false, message }`.
     */
    async getUserCollections(username: string, sortBy: string = 'createdAt_desc') {
        try {
            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}/collections?sortBy=${encodeURIComponent(sortBy)}`, this.getFetchOptions());

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

    /**
     * Lists a user's reviews that the viewer may see, filtered, sorted and paged.
     * @param username The profile's username.
     * @param params Page, sort and filter values; `mediaType` and `externalApiId` are not used here.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async getUserReviews(username: string, params: ReviewFilterParams) {
        try {
            const query = new URLSearchParams();

            query.append('page', params.page.toString());
            if (params.sortBy) query.append('sortBy', params.sortBy);
            if (params.minScore !== undefined) query.append('minScore', params.minScore.toString());
            if (params.maxScore !== undefined) query.append('maxScore', params.maxScore.toString());
            if (params.hasWrittenText) query.append('hasWrittenText', 'true');

            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}/reviews?${query.toString()}`, this.getFetchOptions());

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

    /**
     * Lists the users who follow this user.
     * @param username The profile's username.
     * @returns `{ success: true, data }` with the users, or `{ success: false, message }`.
     */
    async getUserFollowers(username: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}/followers`, this.getFetchOptions());

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

    /**
     * Lists the users this user follows.
     * @param username The profile's username.
     * @returns `{ success: true, data }` with the users, or `{ success: false, message }`.
     */
    async getUserFollowing(username: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}/following`, this.getFetchOptions());

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
