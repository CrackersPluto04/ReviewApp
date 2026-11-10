import { apiFetch } from "./apiClient";
import { isValidEmail, MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH, usernameError } from "../utils/validation";

/**
 * Wraps `api/Auth`: registration, the login flow (including the second step of a 2FA login), logout and the
 * session check. The session itself is an httpOnly cookie the browser sends (`credentials: 'include'`), so no method
 * reads or stores a token.
 *
 * Methods never throw: they resolve to `{ success: true, ... }` or `{ success: false, message }`, where `message`
 * is ready to show. Credentials are checked here first so an obviously invalid form costs no request; the backend
 * validates again.
 */
class AuthService {
    private readonly baseUrl = 'https://localhost:7140/api/Auth';

    /**
     * Creates an account. Does not log in: the caller sends the user to the login page.
     * @param username The wanted username; trimmed, then checked with `usernameError`.
     * @param email The email address; trimmed.
     * @param password The password, within the length limits of `utils/validation`.
     * @param confirmPassword Must equal `password`; checked here only, it is never sent.
     * @returns `{ success, message }`; on success the message is the API's confirmation.
     */
    async register(username: string, email: string, password: string, confirmPassword: string) {
        username = username.trim();
        email = email.trim();

        const validationMessage = this.checkRegisterDatas(username, email, password, confirmPassword);
        if (validationMessage) {
            return { success: false, message: validationMessage };
        }

        try {
            const response = await apiFetch(`${this.baseUrl}/register`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ username, email, password })
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, message: data.message }
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || "Something went wrong. Please try registering again." }
            }
        } catch (error) {
            console.error("Registration error:", error);
            return { success: false, message: 'Network error while registering.' }
        }
    }

    /**
     * Logs in with email and password. The session cookie is set by the response, except for accounts with
     * two-factor authentication.
     * @param email The email address; trimmed.
     * @param password The password; only the upper bound `MAX_PASSWORD_LENGTH` is checked here.
     * @returns `{ success: true, data }` where `data` is the logged-in user, or, for a 2FA account,
     * `{ twoFactorRequired, challengeToken }` to pass to `loginTwoFactor`; otherwise `{ success: false, message }`.
     */
    async login(email: string, password: string) {
        email = email.trim();

        const validationMessage = this.checkLoginDatas(email, password);
        if (validationMessage) {
            return { success: false, message: validationMessage };
        }

        try {
            const response = await apiFetch(`${this.baseUrl}/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ email, password })
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data }
            } else {
                const errorData = await response.json();
                if (errorData.error)
                    return { success: false, message: errorData.error }
                else {
                    console.error("Login error:", errorData);
                    return { success: false, message: "Something went wrong. Please try logging in again." }
                }
            }
        } catch (error) {
            console.error("Login error:", error);
            return { success: false, message: "Something went wrong. Please try logging in again." }
        }
    }

    /**
     * Second step of a 2FA login: `login` answered with a `challengeToken` instead of the session cookie, and this
     * trades it plus the emailed code for the cookie.
     * @param challengeToken The short-lived token from the login response.
     * @param code The emailed code; trimmed.
     * @returns `{ success: true, data }` with the logged-in user, or `{ success: false, message }`.
     */
    async loginTwoFactor(challengeToken: string, code: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/login/2fa`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ challengeToken, code: code.trim() })
            });

            const data = await response.json().catch(() => null);
            if (response.ok)
                return { success: true, data: data };

            return { success: false, message: this.getErrorMessage(data) || "Something went wrong. Please try again." };
        } catch (error) {
            // Never log the request body here - it contains the code
            console.error("Two-factor login error:", error);
            return { success: false, message: "Something went wrong. Please try again." };
        }
    }

    /**
     * Sends a new 2FA login code for a pending login. The API enforces a cooldown between sends.
     * @param challengeToken The token from the login response.
     * @returns `{ success, message }`; on success the message is the API's confirmation.
     */
    async resendTwoFactorCode(challengeToken: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/login/2fa/resend`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ challengeToken })
            });

            const data = await response.json().catch(() => null);
            if (response.ok)
                return { success: true, message: data?.message as string };

            return { success: false, message: this.getErrorMessage(data) || "Something went wrong while sending a new code." };
        } catch (error) {
            console.error("Resend two-factor code error:", error);
            return { success: false, message: "Network error while sending a new code." };
        }
    }

    /** Logs out this browser. Failures are only logged: the caller clears the logged-in state either way. */
    async logout() {
        try {
            await apiFetch(`${this.baseUrl}/logout`, {
                method: 'POST',
                credentials: 'include'
            });
        } catch (error) {
            console.error("Logout error:", error);
        }
    }

    /**
     * Revokes every session of the user (all devices), including this one.
     * @returns `{ success }`, or `{ success: false, message }`.
     */
    async logoutAll() {
        try {
            const response = await apiFetch(`${this.baseUrl}/logout-all`, {
                method: 'POST',
                credentials: 'include'
            });

            if (response.ok)
                return { success: true };

            const errorData = await response.json().catch(() => null);
            return { success: false, message: errorData?.error || "Something went wrong while logging out of all devices." };
        } catch (error) {
            console.error("Logout all error:", error);
            return { success: false, message: "Network error while logging out of all devices." };
        }
    }

    /**
     * Asks the API who the cookie belongs to. Used on startup and by `refreshUser`, and the only way to learn the
     * logged-in state because the cookie is invisible to scripts.
     * @returns `{ success: true, data }` with the user (including tier and entitlements), or `{ success: false }`
     * when logged out or unreachable; no message, since "not logged in" is not an error to show.
     */
    async checkAuth() {
        try {
            const response = await apiFetch(`${this.baseUrl}/check-auth`, {
                method: 'GET',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include'
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            }
            return { success: false };
        } catch (error) {
            console.error("Check auth error:", error);
            return { success: false };
        }
    }

    // Our endpoints return { error }, DataAnnotations failures come back as ProblemDetails ({ errors: { Field: [messages] } })
    private getErrorMessage(errorData: any): string | undefined {
        if (errorData?.error)
            return errorData.error;

        const firstValidationError = Object.values(errorData?.errors ?? {})[0];
        if (Array.isArray(firstValidationError) && firstValidationError.length > 0)
            return firstValidationError[0];

        return undefined;
    }

    private checkRegisterDatas(username: string, email: string, password: string, confirmPassword: string): string {
        if (!username || !email || !password || !confirmPassword)
            return "Please fill all the required fields.";

        const invalidUsername = usernameError(username);
        if (invalidUsername)
            return invalidUsername;

        if (!isValidEmail(email))
            return "Please enter a valid email address.";

        if (password.length < MIN_PASSWORD_LENGTH || password.length > MAX_PASSWORD_LENGTH)
            return `Password must be between ${MIN_PASSWORD_LENGTH} and ${MAX_PASSWORD_LENGTH} characters long.`;

        if (password !== confirmPassword)
            return "Passwords do not match.";

        return "";
    }

    private checkLoginDatas(email: string, password: string): string {
        if (!email || !password)
            return "Please fill all the required fields.";

        if (!isValidEmail(email))
            return "Please enter a valid email address.";

        if (password.length > MAX_PASSWORD_LENGTH)
            return `Password can be at most ${MAX_PASSWORD_LENGTH} characters long.`;

        return "";
    }

}

export const authService = new AuthService();
