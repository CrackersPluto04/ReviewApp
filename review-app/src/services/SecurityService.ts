import { apiFetch } from "./apiClient";

class SecurityService {
    private readonly baseUrl = 'https://localhost:7140/api/Security';

    async getStatus() {
        try {
            const response = await apiFetch(`${this.baseUrl}/status`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            }
            return { success: false, message: await this.getErrorMessage(response, 'Something went wrong while loading your security settings.') };
        } catch (error) {
            console.error('GetSecurityStatus error:', error);
            return { success: false, message: 'Network error while loading your security settings.' };
        }
    }

    // Token from the link in the email, sent with a POST so opening the link alone confirms nothing
    async verifyEmail(token: string) {
        return this.send(`${this.baseUrl}/verify-email`, 'POST', 'Something went wrong while verifying your email.', { token });
    }

    async resendVerification() {
        return this.send(`${this.baseUrl}/resend-verification`, 'POST', 'Something went wrong while sending the verification email.');
    }

    async confirmEmailChange(token: string) {
        return this.send(`${this.baseUrl}/confirm-email-change`, 'POST', 'Something went wrong while confirming your new email.', { token });
    }

    async cancelEmailChange() {
        return this.send(`${this.baseUrl}/pending-email`, 'DELETE', 'Something went wrong while cancelling the email change.');
    }

    // Sends a code to the user's email (checks the password)
    async requestTwoFactorChange(enable: boolean, currentPassword: string) {
        const action = enable ? 'enable' : 'disable';
        return this.send(`${this.baseUrl}/2fa/${action}/request`, 'POST', `Something went wrong while trying to ${action} two-factor authentication.`, { currentPassword });
    }

    async confirmTwoFactorChange(enable: boolean, code: string) {
        const action = enable ? 'enable' : 'disable';
        return this.send(`${this.baseUrl}/2fa/${action}/confirm`, 'POST', `Something went wrong while trying to ${action} two-factor authentication.`, { code });
    }

    /* Helper methods */

    private async send(url: string, method: string, fallback: string, body?: any) {
        try {
            const response = await apiFetch(url, this.getFetchOptions(method, body));

            if (response.ok) {
                const data = await response.json().catch(() => null);
                return { success: true, message: data?.message as string | undefined, data: data };
            }

            return { success: false, message: await this.getErrorMessage(response, fallback) };
        } catch (error) {
            // Never log the request body here - it can contain the password or a code
            console.error('Security request error:', error);
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

export const securityService = new SecurityService();
