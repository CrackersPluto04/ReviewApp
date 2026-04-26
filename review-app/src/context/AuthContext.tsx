import { createContext } from 'preact';
import { useContext, useState, useEffect } from 'preact/hooks';
import { authService } from '../services/AuthService';
import { ReactNode } from 'preact/compat';

interface AuthContextType {
    isLoggedIn: boolean;
    isLoading: boolean;
    setIsLoggedIn: (value: boolean) => void;
    logout: () => Promise<void>;
}

type AuthProviderProps = {
    children: ReactNode;
};

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: AuthProviderProps) {
    const [isLoggedIn, setIsLoggedIn] = useState(false);
    const [isLoading, setIsLoading] = useState(true);

    // Checks the backend for valid cookie
    useEffect(() => {
        const verifyAuth = async () => {
            const isValid = await authService.checkAuth();
            setIsLoggedIn(isValid);
            setIsLoading(false);
        };
        verifyAuth();
    }, []);

    const logout = async () => {
        await authService.logout();
        setIsLoggedIn(false);
    };

    return (
        <AuthContext.Provider value={{ isLoggedIn, isLoading, setIsLoggedIn, logout }}>
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