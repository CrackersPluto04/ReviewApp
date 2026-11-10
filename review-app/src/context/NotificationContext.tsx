import { createContext } from 'preact';
import { useCallback, useContext, useEffect, useRef, useState } from 'preact/hooks';
import { ReactNode } from 'preact/compat';
import { useAuth } from './AuthContext';
import { notificationService } from '../services/NotificationService';
import { setMutationHandler } from '../services/apiClient';

// No real-time channel: the badge is polled while the tab is visible
const POLL_INTERVAL_MS = 45_000;
// Several changes in a row (e.g. a review that unlocks achievements) only refresh once
const MUTATION_REFRESH_DELAY_MS = 800;

interface NotificationContextType {
    /** The number of unseen notifications, 0 while logged out; shown on the header badge. */
    unseenCount: number;
    /** Local adjustment after an action in the dialog, the next refresh corrects any drift. Never goes below 0. */
    adjustUnseenCount: (delta: number) => void;
    /** Sets the count directly, e.g. after "mark all seen". Never goes below 0. */
    setUnseenCount: (count: number) => void;
    /** Fetches the count now. Does nothing while logged out; a failed request keeps the old count. */
    refreshUnseenCount: () => Promise<void>;
}

type NotificationProviderProps = {
    children: ReactNode;
};

const NotificationContext = createContext<NotificationContextType | undefined>(undefined);

/**
 * Holds the unseen notification count. It is loaded on login, polled every 45 seconds while the tab is visible,
 * refreshed when the tab becomes visible again, and refreshed shortly after any successful change the user makes
 * (via `setMutationHandler`), since their own actions can create notifications for them.
 * Mount it inside `AuthProvider`.
 */
export function NotificationProvider({ children }: NotificationProviderProps) {
    const { isLoggedIn } = useAuth();
    const [unseenCount, setUnseenCountState] = useState(0);
    // Read by the async refresh: the state captured when the request started may be outdated when it ends
    const isLoggedInRef = useRef(isLoggedIn);
    isLoggedInRef.current = isLoggedIn;

    const refreshUnseenCount = useCallback(async () => {
        if (!isLoggedInRef.current) return;

        const result = await notificationService.getUnseenCount();
        // The user may have logged out while the request was running
        if (result.success && isLoggedInRef.current)
            setUnseenCountState(result.data ?? 0);
    }, []);

    useEffect(() => {
        if (!isLoggedIn) {
            setUnseenCountState(0);
            return;
        }

        refreshUnseenCount();

        const interval = setInterval(() => {
            if (document.visibilityState === 'visible')
                refreshUnseenCount();
        }, POLL_INTERVAL_MS);

        const handleVisibilityChange = () => {
            if (document.visibilityState === 'visible')
                refreshUnseenCount();
        };
        document.addEventListener('visibilitychange', handleVisibilityChange);

        return () => {
            clearInterval(interval);
            document.removeEventListener('visibilitychange', handleVisibilityChange);
        };
    }, [isLoggedIn]);

    useEffect(() => {
        let timer: ReturnType<typeof setTimeout> | undefined;

        setMutationHandler(() => {
            clearTimeout(timer);
            timer = setTimeout(refreshUnseenCount, MUTATION_REFRESH_DELAY_MS);
        });

        return () => {
            clearTimeout(timer);
            setMutationHandler(null);
        };
    }, []);

    const adjustUnseenCount = (delta: number) => setUnseenCountState(prev => Math.max(0, prev + delta));
    const setUnseenCount = (count: number) => setUnseenCountState(Math.max(0, count));

    return (
        <NotificationContext.Provider value={{ unseenCount, adjustUnseenCount, setUnseenCount, refreshUnseenCount }}>
            {children}
        </NotificationContext.Provider>
    );
}

/**
 * Reads the unseen count and its actions.
 * @returns The context value (see `NotificationContextType`).
 * @throws When used outside a `NotificationProvider`.
 */
export function useNotifications() {
    const context = useContext(NotificationContext);

    if (context === undefined) {
        throw new Error('useNotifications must be used within a NotificationProvider');
    }

    return context;
}
