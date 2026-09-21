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
            const response = await fetch(`${this.baseUrl}/register`, {
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
            const response = await fetch(`${this.baseUrl}/login`, {
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
            await fetch(`${this.baseUrl}/logout`, {
                method: 'POST',
                credentials: 'include'
            });
        } catch (error) {
            console.error("Logout error:", error);
        }
    }

    async checkAuth() {
        try {
            const response = await fetch(`${this.baseUrl}/check-auth`, {
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

        if (!this.checkEmail(email))
            return "Please enter a valid email address.";

        if (password.length < 8)
            return "Password must be at least 8 characters long.";

        if (password !== confirmPassword)
            return "Passwords do not match.";

        return "";
    }

    // Validates login datas
    private checkLoginDatas(email: string, password: string): string {
        if (!email || !password)
            return "Please fill all the required fields.";

        if (!this.checkEmail(email))
            return "Please enter a valid email address.";

        return "";
    }

    // Simple email format validation
    private checkEmail(email: string): boolean {
        // local@domain.tld - no whitespace, and no empty domain labels (a@b..c, a@.b, a@b.)
        const emailRegex = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;
        return email.length <= 254 && emailRegex.test(email);
    }
}

export const authService = new AuthService();