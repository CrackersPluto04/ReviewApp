import { createContext } from 'preact';
import { useContext, useState, useEffect } from 'preact/hooks';
import { authService } from '../services/AuthService';
import { setUnauthorizedHandler } from '../services/apiClient';
import { ReactNode } from 'preact/compat';
import { SubscriptionTier } from '../types/types';

/**
 * The logged-in user as returned by login and check-auth. The session itself is an httpOnly cookie that scripts
 * cannot read, so this object is the only client-side trace of it.
 */
export interface AuthUser {
    id: number;
    username: string;
    profilePictureUrl?: string;
    /** Drives the non-blocking email verification banner; call `refreshUser` after anything that changes it. */
    isEmailConfirmed: boolean;
    /** The subscription state at the last check-auth; the backend checks access on every request anyway. */
    tier: SubscriptionTier;
    isPremium: boolean;
    /** Feature keys the user has access to, see `FEATURE_KEYS` in `utils/subscription.ts`. */
    entitlements: string[];
}

interface AuthContextType {
    /** The logged-in user, or `null` when logged out (also while `isLoading`). */
    user: AuthUser | null;
    isLoggedIn: boolean;
    /** `true` until the first check-auth answered, so pages don't flash the logged-out state or redirect too early. */
    isLoading: boolean;
    /** Sets the user directly, e.g. with the login response. */
    setUser: (user: AuthUser | null) => void;
    /** Reloads the user from the backend (e.g. after the email was verified); logs out locally if the cookie is invalid. */
    refreshUser: () => Promise<void>;
    /** Ends the session on the backend, then clears the user. */
    logout: () => Promise<void>;
}

type AuthProviderProps = {
    children: ReactNode;
};

const AuthContext = createContext<AuthContextType | undefined>(undefined);

/**
 * Provides the auth state to the whole app. On mount it asks the backend whether the cookie is valid, and it
 * drops the user on any 401 from the API (via `setUnauthorizedHandler`): the session expired, was ended on
 * another device or was revoked by a password change.
 * Mount it above everything that calls `useAuth`.
 */
export function AuthProvider({ children }: AuthProviderProps) {
    const [user, setUser] = useState<AuthUser | null>(null);
    const [isLoading, setIsLoading] = useState(true);

    const refreshUser = async () => {
        const result = await authService.checkAuth();
        if (result.success)
            setUser(result.data);
        else
            setUser(null);
    };

    useEffect(() => {
        const verifyAuth = async () => {
            setIsLoading(true);
            await refreshUser();
            setIsLoading(false);
        };

        verifyAuth();
    }, []);

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
        <AuthContext.Provider value={{ user, isLoggedIn, isLoading, setUser, refreshUser, logout }}>
            {children}
        </AuthContext.Provider>
    );
}

/**
 * Reads the auth state.
 * @returns The context value (see `AuthContextType`).
 * @throws When used outside an `AuthProvider`.
 */
export function useAuth() {
    const context = useContext(AuthContext);

    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }

    return context;
}
