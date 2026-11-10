import { apiFetch } from "./apiClient";
import { RecognitionAllowanceDto, RecognitionItemsDto, RecognitionResponseDto } from "../types/types";

/**
 * The outcome of a recognition request. Unlike the other services it carries the HTTP status and the API's
 * machine readable reason, because the page reacts differently to each failure (upsell, "busy", daily limit, expired list).
 */
export interface RecognitionResult<T> {
    success: boolean;
    data?: T;
    /** Text to show on failure; empty on success. */
    message: string;
    /** The HTTP status of a failed response; absent for a network failure or a cancel. */
    status?: number;
    /** The API's machine readable reason: busy, daily_limit, recognition_unavailable, source_unavailable, not_found, premium_required. */
    code?: string;
    /** When the daily allowance resets (ISO date); sent with `daily_limit`. */
    resetsAt?: string;
    /** The caller cancelled the request through its `AbortSignal`; not an error to show. */
    aborted?: boolean;
}

/**
 * Wraps `api/Recognition` (camera recognition, a Premium feature): the photo goes up as multipart/form-data and
 * "Show more" pages through the list the API stored for that recognition.
 * Methods never throw; failures come back as a `RecognitionResult` with `success: false`.
 */
class RecognitionService {
    private readonly baseUrl = 'https://localhost:7140/api/Recognition';

    /**
     * Tells whether the feature is on and what is left of the user's daily allowance. Open to Free users too
     * (they get `premium: false` and no counters), so the page can show the entry point and the upsell.
     * @returns The allowance.
     */
    async getAllowance(): Promise<RecognitionResult<RecognitionAllowanceDto>> {
        return this.send(`${this.baseUrl}/allowance`, { method: 'GET', credentials: 'include' }, 'Something went wrong while loading camera recognition.');
    }

    /**
     * Recognises a photo. Counts against the daily allowance and can take a while (an AI call plus the TMDb /
     * Spotify lookups), hence the abort signal.
     * @param photo The photo, already downscaled (see `utils/image`); sent as the `image` part.
     * @param signal Lets the user stop waiting; the result is then discarded and `aborted` is set.
     * @returns The first part of the ranked list and the recognition ID to page with, or a failure whose `code`
     * tells why (402 `premium_required`, 409 `busy`, 429 `daily_limit`, 503 `recognition_unavailable` / `source_unavailable`).
     */
    async recognize(photo: Blob, signal?: AbortSignal): Promise<RecognitionResult<RecognitionResponseDto>> {
        const form = new FormData();
        form.append('image', photo, 'photo.jpg');
        // No Content-Type header: the browser sets multipart/form-data with its boundary
        return this.send(this.baseUrl, { method: 'POST', credentials: 'include', body: form, signal },
            'Something went wrong while recognising the photo.');
    }

    /**
     * Loads more of a recognition's list ("Show more").
     * @param recognitionId The ID from `recognize`.
     * @param offset How many items the page already shows.
     * @param count How many to load; the API default when omitted.
     * @returns The next items, or a failure; `not_found` means the list expired (the API keeps it for a limited time).
     */
    async getItems(recognitionId: string, offset: number, count?: number): Promise<RecognitionResult<RecognitionItemsDto>> {
        const params = new URLSearchParams({ offset: String(offset) });
        if (count !== undefined) params.set('count', String(count));
        return this.send(`${this.baseUrl}/${encodeURIComponent(recognitionId)}/items?${params}`, { method: 'GET', credentials: 'include' },
            'Something went wrong while loading more suggestions.');
    }

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
