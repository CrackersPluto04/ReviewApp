import { useEffect, useRef, useState } from "preact/hooks";
import { Alert, Box, Button, CircularProgress, Stack, Typography } from "@mui/material";
import WorkspacePremiumIcon from "@mui/icons-material/WorkspacePremium";
import { useNavigate, useSearchParams } from "react-router-dom";
import { subscriptionService } from "../services/SubscriptionService";
import { useAuth } from "../context/AuthContext";
import { PREMIUM_COLOR } from "../utils/subscription";
import { PleaseLogin } from "../components/PleaseLogin";

const POLL_INTERVAL_MS = 2000;
const POLL_TIMEOUT_MS = 30_000;

type ReturnState = 'checking' | 'success' | 'pending' | 'cancelled' | 'error';

const TITLES: Record<ReturnState, string> = {
    checking: 'Your subscription',
    success: 'Welcome to Premium!',
    pending: 'Your subscription',
    cancelled: 'Checkout cancelled',
    error: 'Your subscription'
};

// Where the payment provider sends the user back after checkout. The redirect proves nothing:
// the backend is asked to check with the provider, then polled until Premium shows up (the
// provider's notification can arrive a little later).
export function SubscriptionReturnPage() {
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();
    const { isLoggedIn, isLoading, refreshUser } = useAuth();

    const cancelled = searchParams.get('status') !== 'success';
    const [state, setState] = useState<ReturnState>(cancelled ? 'cancelled' : 'checking');
    const [message, setMessage] = useState('');
    const startedRef = useRef(false);

    useEffect(() => {
        if (cancelled || isLoading || !isLoggedIn || startedRef.current) return;
        startedRef.current = true;

        let stopped = false;
        let timer: ReturnType<typeof setTimeout> | undefined;
        const deadline = Date.now() + POLL_TIMEOUT_MS;

        const finish = async (next: ReturnState, text = '') => {
            if (stopped) return;
            setState(next);
            setMessage(text);
            if (next === 'success') await refreshUser();
        };

        const poll = async () => {
            const result = await subscriptionService.getStatus();
            if (stopped) return;

            if (result.success && result.data?.isPremium)
                await finish('success');
            else if (Date.now() >= deadline)
                await finish('pending');
            else
                timer = setTimeout(poll, POLL_INTERVAL_MS);
        };

        const start = async () => {
            const result = await subscriptionService.sync();
            if (stopped) return;

            if (!result.success)
                await finish('error', result.message ?? 'Something went wrong.');
            else if (result.data?.isPremium)
                await finish('success');
            else
                timer = setTimeout(poll, POLL_INTERVAL_MS);
        };

        void start();
        return () => {
            stopped = true;
            clearTimeout(timer);
        };
    }, [cancelled, isLoading, isLoggedIn]);

    if (!cancelled && !isLoading && !isLoggedIn)
        return <PleaseLogin message="Log in to see your subscription." />;

    return <Stack spacing={2} alignItems="center" sx={{ maxWidth: 520, mx: 'auto', textAlign: 'center' }}>
        <WorkspacePremiumIcon sx={{ fontSize: 56, color: state === 'success' ? PREMIUM_COLOR : 'text.disabled' }} />
        <Typography variant="h4" component="h1">{TITLES[state]}</Typography>

        {state === 'checking' && (
            <Box display="flex" alignItems="center" gap={1.5} role="status">
                <CircularProgress size={24} />
                <Typography color="text.secondary">Confirming your payment...</Typography>
            </Box>
        )}

        {state === 'success' && (
            <Alert severity="success" sx={{ width: '100%', textAlign: 'left' }}>
                Your Premium features are unlocked. Thanks for supporting the app!
            </Alert>
        )}

        {state === 'pending' && (
            <Alert severity="info" sx={{ width: '100%', textAlign: 'left' }}>
                Your payment is still being processed. This can take a minute; you'll get a notification once Premium is active.
            </Alert>
        )}

        {state === 'cancelled' && (
            <Alert severity="info" sx={{ width: '100%', textAlign: 'left' }}>
                Nothing was charged. You can subscribe whenever you like.
            </Alert>
        )}

        {state === 'error' && (
            <Alert severity="error" sx={{ width: '100%', textAlign: 'left' }}>{message}</Alert>
        )}

        {state !== 'checking' && (
            <Stack direction="row" spacing={1}>
                <Button variant={state === 'success' ? 'outlined' : 'contained'} onClick={() => navigate('/premium')}>
                    {state === 'cancelled' ? 'Back to Premium' : 'View my subscription'}
                </Button>
                <Button onClick={() => navigate('/home')}>Go to the home page</Button>
            </Stack>
        )}
    </Stack>
}
