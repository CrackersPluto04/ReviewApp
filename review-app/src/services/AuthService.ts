import { apiFetch } from "./apiClient";
import { isValidEmail, MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from "../utils/validation";

class AuthService {
    private readonly baseUrl = 'https://localhost:7140/api/Auth';

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

    // Revokes every session of the user (all devices), including this one
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

    /**
     * Helper methods for validating input data before sending requests to the backend
     */

    // Validates registration datas
    private checkRegisterDatas(username: string, email: string, password: string, confirmPassword: string): string {
        if (!username || !email || !password || !confirmPassword)
            return "Please fill all the required fields.";

        if (username.length < 3 || username.length > 20)
            return "Username must be between 3 and 20 characters.";

        if (!isValidEmail(email))
            return "Please enter a valid email address.";

        if (password.length < MIN_PASSWORD_LENGTH || password.length > MAX_PASSWORD_LENGTH)
            return `Password must be between ${MIN_PASSWORD_LENGTH} and ${MAX_PASSWORD_LENGTH} characters long.`;

        if (password !== confirmPassword)
            return "Passwords do not match.";

        return "";
    }

    // Validates login datas
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