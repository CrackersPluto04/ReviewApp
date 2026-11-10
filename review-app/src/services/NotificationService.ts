import { apiFetch } from "./apiClient";
import { NotificationQueryParams } from "../types/types";

/**
 * Wraps `api/Notification`: the logged-in user's own notifications (someone else's id answers 404).
 * Methods never throw; they resolve to `{ success, data }` / `{ success }` or `{ success: false, message }`.
 */
class NotificationService {
    private readonly baseUrl = 'https://localhost:7140/api/Notification';

    /**
     * Loads one page of notifications with keyset paging, so a page stays stable while new ones arrive.
     * @param params The seen / unseen filter and sort; `afterId` is the ID of the last notification of the
     * previous page, and `pageSize` defaults to 20 on the API and is capped at 50.
     * @returns `{ success: true, data }` with the page, or `{ success: false, message }`.
     */
    async getNotifications(params: NotificationQueryParams) {
        try {
            const query = new URLSearchParams();

            query.append('status', params.status);
            query.append('sort', params.sort);
            if (params.afterId !== undefined) query.append('afterId', params.afterId.toString());
            if (params.pageSize !== undefined) query.append('pageSize', params.pageSize.toString());

            const response = await apiFetch(`${this.baseUrl}?${query.toString()}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                return { success: false, message: await this.getErrorMessage(response, 'Something went wrong while getting your notifications.') }
            }
        } catch (error) {
            console.error('GetNotifications error:', error);
            return { success: false, message: 'Network error while reaching notifications.' }
        }
    }

    /**
     * Counts the unseen notifications. Cheap on purpose: the header badge polls it.
     * @returns `{ success: true, data }` with the count, or `{ success: false }` with no message, because a failed
     * poll is not worth showing; the next poll tries again.
     */
    async getUnseenCount() {
        try {
            const response = await apiFetch(`${this.baseUrl}/unseen-count`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data.count as number };
            }
            return { success: false };
        } catch (error) {
            console.error('GetUnseenCount error:', error);
            return { success: false };
        }
    }

    /**
     * Marks one notification seen or unseen.
     * @param id The notification's ID.
     * @param isSeen The new state.
     * @returns `{ success }` or `{ success: false, message }`.
     */
    async setSeen(id: number, isSeen: boolean) {
        return this.send(`${this.baseUrl}/${id}/seen`, 'PATCH', 'Something went wrong while updating the notification.', { isSeen });
    }

    /**
     * Marks every notification seen.
     * @returns `{ success }` or `{ success: false, message }`.
     */
    async markAllSeen() {
        return this.send(`${this.baseUrl}/seen-all`, 'POST', 'Something went wrong while marking your notifications as seen.');
    }

    /**
     * Deletes one notification. Already gone counts as success (see `send`).
     * @param id The notification's ID.
     * @returns `{ success }` or `{ success: false, message }`.
     */
    async deleteNotification(id: number) {
        return this.send(`${this.baseUrl}/${id}`, 'DELETE', 'Something went wrong while deleting the notification.');
    }

    /**
     * Deletes every seen notification.
     * @returns `{ success }` or `{ success: false, message }`.
     */
    async deleteSeen() {
        return this.send(`${this.baseUrl}/seen`, 'DELETE', 'Something went wrong while clearing your seen notifications.');
    }

    private async send(url: string, method: string, fallback: string, body?: any) {
        try {
            const response = await apiFetch(url, this.getFetchOptions(method, body));

            if (response.ok)
                return { success: true };

            // Already gone (deleted in another tab): nothing left to do
            if (response.status === 404 && method === 'DELETE')
                return { success: true };

            return { success: false, message: await this.getErrorMessage(response, fallback) };
        } catch (error) {
            console.error('Notification request error:', error);
            return { success: false, message: 'Network error while reaching notifications.' };
        }
    }

    private async getErrorMessage(response: Response, fallback: string): Promise<string> {
        if (response.status === 401)
            return 'Your session has expired. Please log in again.';

        const errorData = await response.json().catch(() => null);
        return errorData?.error || fallback;
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

export const notificationService = new NotificationService();
