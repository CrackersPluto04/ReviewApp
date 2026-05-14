class UserService {
    private readonly baseUrl = 'https://localhost:7140/api/User';

    async getUserCollections(username: string, sortBy: string = 'createdAt_desc') {
        try {
            const response = await fetch(`${this.baseUrl}/${username}/collections?sortBy=${sortBy}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting user collections.' }
            }
        } catch (error) {
            console.error('GetUserCollections error:', error);
            return { success: false, message: 'Network error while reaching user collections.' }
        }
    }

    /* Helper methods */

    // Create fetch options for different HTTP methods and request bodies
    private getFetchOptions(method: string = 'GET', body?: any): RequestInit {
        return {
            method,
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: body ? JSON.stringify(body) : undefined
        };
    }
}

export const userService = new UserService();