import { apiFetch } from "./apiClient";
import { NotificationQueryParams } from "../types/types";

class NotificationService {
    private readonly baseUrl = 'https://localhost:7140/api/Notification';

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

    // Polled by the header badge
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

    async setSeen(id: number, isSeen: boolean) {
        return this.send(`${this.baseUrl}/${id}/seen`, 'PATCH', 'Something went wrong while updating the notification.', { isSeen });
    }

    async markAllSeen() {
        return this.send(`${this.baseUrl}/seen-all`, 'POST', 'Something went wrong while marking your notifications as seen.');
    }

    async deleteNotification(id: number) {
        return this.send(`${this.baseUrl}/${id}`, 'DELETE', 'Something went wrong while deleting the notification.');
    }

    async deleteSeen() {
        return this.send(`${this.baseUrl}/seen`, 'DELETE', 'Something went wrong while clearing your seen notifications.');
    }

    /* Helper methods */

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
