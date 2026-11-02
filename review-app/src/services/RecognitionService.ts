import { apiFetch } from "./apiClient";
import { RecognitionAllowanceDto, RecognitionItemsDto, RecognitionResponseDto } from "../types/types";

export interface RecognitionResult<T> {
    success: boolean;
    data?: T;
    message: string;
    status?: number;
    // The API's machine readable reason: busy, daily_limit, recognition_unavailable, source_unavailable, not_found, premium_required
    code?: string;
    resetsAt?: string; // with daily_limit
    aborted?: boolean; // the caller cancelled
}

// Camera recognition: the photo goes up as multipart/form-data, "Show more" pages through the stored list
class RecognitionService {
    private readonly baseUrl = 'https://localhost:7140/api/Recognition';

    // Whether the feature is on, and the user's daily allowance (Free users get premium: false, no counters)
    async getAllowance(): Promise<RecognitionResult<RecognitionAllowanceDto>> {
        return this.send(`${this.baseUrl}/allowance`, { method: 'GET', credentials: 'include' }, 'Something went wrong while loading camera recognition.');
    }

    // The signal lets the user stop waiting; the result is then discarded
    async recognize(photo: Blob, signal?: AbortSignal): Promise<RecognitionResult<RecognitionResponseDto>> {
        const form = new FormData();
        form.append('image', photo, 'photo.jpg');
        // No Content-Type header: the browser sets multipart/form-data with its boundary
        return this.send(this.baseUrl, { method: 'POST', credentials: 'include', body: form, signal },
            'Something went wrong while recognising the photo.');
    }

    async getItems(recognitionId: string, offset: number, count?: number): Promise<RecognitionResult<RecognitionItemsDto>> {
        const params = new URLSearchParams({ offset: String(offset) });
        if (count !== undefined) params.set('count', String(count));
        return this.send(`${this.baseUrl}/${encodeURIComponent(recognitionId)}/items?${params}`, { method: 'GET', credentials: 'include' },
            'Something went wrong while loading more suggestions.');
    }

    /* Helper methods */

    private async send<T>(url: string, init: RequestInit, fallback: string): Promise<RecognitionResult<T>> {
        try {
            const response = await apiFetch(url, init);
            if (response.ok)
                return { success: true, message: '', data: await response.json() };

            if (response.status === 401)
                return { success: false, status: 401, message: 'Your session has expired. Please log in again.' };

            const errorData = await response.json().catch(() => null);
            return {
                success: false,
                status: response.status,
                code: typeof errorData?.code === 'string' ? errorData.code : undefined,
                resetsAt: typeof errorData?.resetsAt === 'string' ? errorData.resetsAt : undefined,
                message: typeof errorData?.error === 'string' ? errorData.error : fallback
            };
        } catch (error) {
            // By name: an AbortError isn't always the page's own DOMException class
            if ((error as { name?: string })?.name === 'AbortError')
                return { success: false, aborted: true, message: 'Cancelled.' };

            console.error('Recognition request error:', error);
            return { success: false, message: 'Network error. Please check your connection and try again.' };
        }
    }
}

export const recognitionService = new RecognitionService();
