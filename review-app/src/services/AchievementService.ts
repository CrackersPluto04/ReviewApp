import { AchievementDto } from "../types/types";
import { apiFetch } from "./apiClient";

/**
 * Wraps `api/Achievement`. Unlocks themselves are not fetched here: they arrive in the
 * `X-Unlocked-Achievements` header of whatever request caused them (see `apiFetch`).
 * Methods never throw; they resolve to `{ success, data }` or `{ success: false, message }`.
 */
class AchievementService {
    private readonly baseUrl = 'https://localhost:7140/api/Achievement';

    /**
     * Loads every achievement with the user's progress. The endpoint is public, so any profile can be viewed.
     * The list is not paged: the profile tab filters and sorts it client-side.
     * @param username The profile's username.
     * @returns `{ success: true, data }` with an `AchievementDto[]`, or `{ success: false, message }`.
     */
    async getUserAchievements(username: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${encodeURIComponent(username)}`, this.getFetchOptions());

            if (response.ok) {
                const data: AchievementDto[] = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting achievements.' }
            }
        } catch (error) {
            console.error('GetUserAchievements error:', error);
            return { success: false, message: 'Network error while reaching achievements.' }
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

export const achievementService = new AchievementService();
