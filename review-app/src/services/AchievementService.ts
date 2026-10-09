import { AchievementDto } from "../types/types";
import { apiFetch } from "./apiClient";

class AchievementService {
    private readonly baseUrl = 'https://localhost:7140/api/Achievement';

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

export const achievementService = new AchievementService();
