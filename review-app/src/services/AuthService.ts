class AuthService {
    private readonly successObject = { success: true, message: "" };

    async register(username: string, email: string, password: string) {
        const validationMessage = this.checkRegisterDatas(username, email, password);
        if (validationMessage) {
            return { success: false, message: validationMessage };
        }

        try {
            const response = await fetch('https://localhost:7140/api/Auth/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username, email, password })
            });

            if (response.ok) {
                const data = await response.json();
                this.successObject.success = true;
                this.successObject.message = data.message;
            } else {
                const errorData = await response.json();
                this.successObject.success = false;
                if (errorData.error)
                    this.successObject.message = errorData.error;
                else {
                    this.successObject.message = "Something went wrong. Please try registering again.";
                    console.error("Registration error:", errorData);
                }
            }
        } catch (error) {
            console.error("Registration error:", error);
            this.successObject.success = false;
            this.successObject.message = "Something went wrong. Please try registering again.";
        }

        return this.successObject;
    }

    async login(email: string, password: string) {
        const validationMessage = this.checkLoginDatas(email, password);
        if (validationMessage) {
            return { success: false, message: validationMessage };
        }

        try {
            const response = await fetch('https://localhost:7140/api/Auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });

            if (response.ok) {
                const data = await response.json();
                localStorage.setItem('jwt_token', data.token);
                this.successObject.success = true;
                this.successObject.message = data.message;
            } else {
                const errorData = await response.json();
                this.successObject.success = false;
                if (errorData.error)
                    this.successObject.message = errorData.error;
                else {
                    this.successObject.message = "Something went wrong. Please try login again.";
                    console.error("Login error:", errorData);
                }
            }
        } catch (error) {
            console.error("Login error:", error);
            this.successObject.success = false;
            this.successObject.message = "Something went wrong. Please try login again.";
        }

        return this.successObject;
    }

    private checkRegisterDatas(username: string, email: string, password: string): string {
        if (!username || !email || !password)
            return "Please fill all the required fields.";

        if (username.length < 3 || username.length > 20)
            return "Username must be between 3 and 20 characters.";

        if (!this.checkEmail(email))
            return "Please enter a valid email address.";

        if (password.length < 8)
            return "Password must be at least 8 characters long.";

        return "";
    }

    private checkLoginDatas(email: string, password: string): string {
        if (!email || !password)
            return "Please fill all the required fields.";

        if (!this.checkEmail(email))
            return "Please enter a valid email address.";

        return "";
    }

    private checkEmail(email: string): boolean {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        return emailRegex.test(email);
    }
}

export const authService = new AuthService();