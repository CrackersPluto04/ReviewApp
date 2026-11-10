import { apiFetch } from "./apiClient";

/**
 * Wraps `api/Security`: email verification, the pending email change and two-factor authentication (enable and
 * disable are each two steps: the password is checked and a code is emailed, then the code is confirmed).
 * Methods never throw; they resolve to `{ success, message?, data? }` or `{ success: false, message }`, the message
 * being ready to show. Request bodies are never logged: they hold passwords and codes.
 */
class SecurityService {
    private readonly baseUrl = 'https://localhost:7140/api/Security';

    /**
     * Loads the account's security state: the only source of the email, the pending email change and the 2FA state.
     * @returns `{ success: true, data }` with the status, or `{ success: false, message }`.
     */
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

    /**
     * Confirms the email address with the token from the emailed link. Sent as a POST by the link's page, never by
     * the link itself, so mail scanners that prefetch links confirm nothing.
     * @param token The token from the link.
     * @returns `{ success, message }`.
     */
    async verifyEmail(token: string) {
        return this.send(`${this.baseUrl}/verify-email`, 'POST', 'Something went wrong while verifying your email.', { token });
    }

    /**
     * Sends the verification email again.
     * @returns `{ success, message }`. The API enforces a cooldown between sends and does not report it.
     */
    async resendVerification() {
        return this.send(`${this.baseUrl}/resend-verification`, 'POST', 'Something went wrong while sending the verification email.');
    }

    /**
     * Confirms a requested email change with the token from the link sent to the new address. Confirming revokes
     * every session; the browser that confirms keeps a fresh one if it belongs to the same user.
     * @param token The token from the link.
     * @returns `{ success, message }`.
     */
    async confirmEmailChange(token: string) {
        return this.send(`${this.baseUrl}/confirm-email-change`, 'POST', 'Something went wrong while confirming your new email.', { token });
    }

    /**
     * Cancels the pending email change.
     * @returns `{ success, message }`.
     */
    async cancelEmailChange() {
        return this.send(`${this.baseUrl}/pending-email`, 'DELETE', 'Something went wrong while cancelling the email change.');
    }

    /**
     * First step of enabling or disabling 2FA: checks the password and emails a code.
     * @param enable `true` to enable, `false` to disable.
     * @param currentPassword The account password.
     * @returns `{ success, message }`.
     */
    async requestTwoFactorChange(enable: boolean, currentPassword: string) {
        const action = enable ? 'enable' : 'disable';
        return this.send(`${this.baseUrl}/2fa/${action}/request`, 'POST', `Something went wrong while trying to ${action} two-factor authentication.`, { currentPassword });
    }

    /**
     * Second step of enabling or disabling 2FA: confirms the emailed code.
     * @param enable `true` to enable, `false` to disable; must match the request step.
     * @param code The emailed code.
     * @returns `{ success, message }`.
     */
    async confirmTwoFactorChange(enable: boolean, code: string) {
        const action = enable ? 'enable' : 'disable';
        return this.send(`${this.baseUrl}/2fa/${action}/confirm`, 'POST', `Something went wrong while trying to ${action} two-factor authentication.`, { code });
    }

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
