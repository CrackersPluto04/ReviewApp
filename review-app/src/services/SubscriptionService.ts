import { apiFetch } from "./apiClient";

/**
 * Wraps `api/Subscription` (the Premium plan). Payment happens on the provider's hosted pages: `startCheckout`
 * and `openPortal` return a URL to send the browser to, and the local subscription only changes from what the
 * provider reports, never from a call made here.
 * Methods never throw; they resolve to `{ success, message?, data? }` or `{ success: false, status?, message }`.
 */
class SubscriptionService {
    private readonly baseUrl = 'https://localhost:7140/api/Subscription';

    /**
     * Loads the plan (price, interval, features) for the plans page. Anonymous, so visitors can see it too.
     * @returns `{ success: true, data }` with the plan.
     */
    async getPlan() {
        return this.send(`${this.baseUrl}/plan`, 'GET', 'Something went wrong while loading the plans.');
    }

    /**
     * Loads the logged-in user's subscription: tier, state and dates.
     * @returns `{ success: true, data }` with the status.
     */
    async getStatus() {
        return this.send(`${this.baseUrl}/status`, 'GET', 'Something went wrong while loading your subscription.');
    }

    /**
     * Starts a checkout.
     * @returns `{ success: true, data }` where `data` holds the provider's checkout URL to redirect to. The
     * `platform` tells the API which return page the provider sends the user back to.
     */
    async startCheckout() {
        return this.send(`${this.baseUrl}/checkout`, 'POST', 'Something went wrong while starting the checkout.', { platform: 'web' });
    }

    /**
     * Opens the provider's customer portal (payment details, invoices).
     * @returns `{ success: true, data }` where `data` holds the portal URL to redirect to.
     */
    async openPortal() {
        return this.send(`${this.baseUrl}/portal`, 'POST', 'Something went wrong while opening the payment settings.', { platform: 'web' });
    }

    /**
     * Stops the subscription from renewing; Premium stays until the end of the paid period.
     * @returns `{ success, message }`.
     */
    async cancel() {
        return this.send(`${this.baseUrl}/cancel`, 'POST', 'Something went wrong while cancelling your subscription.');
    }

    /**
     * Undoes `cancel` while the paid period is still running.
     * @returns `{ success, message }`.
     */
    async resume() {
        return this.send(`${this.baseUrl}/resume`, 'POST', 'Something went wrong while resuming your subscription.');
    }

    /**
     * Asks the backend to check with the payment provider and update the local record. The return page calls it
     * because it never trusts the redirect alone, and the provider's webhook may arrive later.
     * @returns `{ success, message }`.
     */
    async sync() {
        return this.send(`${this.baseUrl}/sync`, 'POST', 'Something went wrong while checking your subscription.');
    }

    private async send(url: string, method: string, fallback: string, body?: any) {
        try {
            const response = await apiFetch(url, this.getFetchOptions(method, body));

            if (response.ok) {
                const data = await response.json().catch(() => null);
                return { success: true, message: data?.message as string | undefined, data: data };
            }

            return { success: false, status: response.status, message: await this.getErrorMessage(response, fallback) };
        } catch (error) {
            console.error('Subscription request error:', error);
            return { success: false, message: 'Network error. Please try again.' };
        }
    }

    // Our endpoints return { error }, DataAnnotations failures come back as ProblemDetails ({ errors: { Field: [messages] } })
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

export const subscriptionService = new SubscriptionService();
