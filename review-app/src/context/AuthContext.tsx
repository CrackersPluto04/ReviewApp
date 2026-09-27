import { createContext } from 'preact';
import { useContext, useState, useEffect } from 'preact/hooks';
import { authService } from '../services/AuthService';
import { setUnauthorizedHandler } from '../services/apiClient';
import { ReactNode } from 'preact/compat';

export interface AuthUser {
    id: number;
    username: string;
    profilePictureUrl?: string;
}

interface AuthContextType {
    user: AuthUser | null;
    isLoggedIn: boolean;
    isLoading: boolean;
    setUser: (user: AuthUser | null) => void;
    logout: () => Promise<void>;
}

type AuthProviderProps = {
    children: ReactNode;
};

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: AuthProviderProps) {
    const [user, setUser] = useState<AuthUser | null>(null);
    const [isLoading, setIsLoading] = useState(true);

    // Checks the backend for valid cookie and gets user details
    useEffect(() => {
        const verifyAuth = async () => {
            setIsLoading(true);

            const result = await authService.checkAuth();
            if (result.success)
                setUser(result.data);
            else
                setUser(null);

            setIsLoading(false);
        };

        verifyAuth();
    }, []);

    // Any 401 from the API means the session is gone (expired, logged out elsewhere or revoked by a password change)
    useEffect(() => {
        setUnauthorizedHandler(() => setUser(null));
        return () => setUnauthorizedHandler(null);
    }, []);

    const logout = async () => {
        await authService.logout();
        setUser(null);
    };

    const isLoggedIn = user !== null;

    return (
        <AuthContext.Provider value={{ user, isLoggedIn, isLoading, setUser, logout }}>
            {children}
        </AuthContext.Provider>
    );
}

// Custom hook so components can grab the auth state easily
export function useAuth() {
    const context = useContext(AuthContext);

    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }

    return context;
}