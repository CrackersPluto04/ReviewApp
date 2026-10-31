import { apiFetch } from "./apiClient";

// Payment happens on the provider's hosted pages: checkout / portal return a URL to send the browser to
class SubscriptionService {
    private readonly baseUrl = 'https://localhost:7140/api/Subscription';

    async getPlan() {
        return this.send(`${this.baseUrl}/plan`, 'GET', 'Something went wrong while loading the plans.');
    }

    async getStatus() {
        return this.send(`${this.baseUrl}/status`, 'GET', 'Something went wrong while loading your subscription.');
    }

    async startCheckout() {
        return this.send(`${this.baseUrl}/checkout`, 'POST', 'Something went wrong while starting the checkout.', { platform: 'web' });
    }

    async openPortal() {
        return this.send(`${this.baseUrl}/portal`, 'POST', 'Something went wrong while opening the payment settings.', { platform: 'web' });
    }

    async cancel() {
        return this.send(`${this.baseUrl}/cancel`, 'POST', 'Something went wrong while cancelling your subscription.');
    }

    async resume() {
        return this.send(`${this.baseUrl}/resume`, 'POST', 'Something went wrong while resuming your subscription.');
    }

    // Asks the backend to check with the payment provider (after returning from checkout)
    async sync() {
        return this.send(`${this.baseUrl}/sync`, 'POST', 'Something went wrong while checking your subscription.');
    }

    // Placeholder Premium endpoint, proves the paywall end to end (a Free user gets a 402)
    async tryEntitlementPreview() {
        return this.send(`${this.baseUrl}/entitlement-preview`, 'GET', 'Something went wrong while opening the preview.');
    }

    /* Helper methods */

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
