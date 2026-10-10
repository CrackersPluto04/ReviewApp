import { useEffect, useRef, useState } from "preact/hooks";
import { Alert, Box, Button, CircularProgress, Stack, Typography } from "@mui/material";
import MarkEmailReadOutlinedIcon from "@mui/icons-material/MarkEmailReadOutlined";
import { useNavigate, useSearchParams } from "react-router-dom";
import { securityService } from "../services/SecurityService";
import { useAuth } from "../context/AuthContext";

type EmailLinkPageProps = {
    // verify: link from the welcome / verification email, change: link sent to the new address
    mode: 'verify' | 'change';
};

// Landing page of the links in account emails. Opening the page alone confirms nothing:
// the token is submitted with a POST from here, so email scanners that prefetch links can't use it up.
export function EmailLinkPage({ mode }: EmailLinkPageProps) {
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();
    const { isLoggedIn, refreshUser } = useAuth();

    const token = searchParams.get('token') ?? '';
    const [state, setState] = useState<'loading' | 'success' | 'error'>('loading');
    const [message, setMessage] = useState('');
    const [resendMessage, setResendMessage] = useState('');
    const [resendSuccess, setResendSuccess] = useState(true);
    const submittedRef = useRef(false);

    const title = mode === 'verify' ? 'Email verification' : 'Confirm new email address';

    useEffect(() => {
        // Single use token, never submit it twice
        if (submittedRef.current) return;
        submittedRef.current = true;

        const submit = async () => {
            if (!token) {
                setState('error');
                setMessage('This link is incomplete. Please open the link from the email again.');
                return;
            }

            const result = mode === 'verify'
                ? await securityService.verifyEmail(token)
                : await securityService.confirmEmailChange(token);

            setState(result.success ? 'success' : 'error');
            setMessage(result.message ?? (result.success ? 'Done!' : 'Something went wrong.'));

            // Verified flag / revoked sessions: reload who is logged in
            if (result.success) await refreshUser();
        };

        submit();
    }, []);

    const handleResend = async () => {
        const result = await securityService.resendVerification();
        setResendSuccess(result.success);
        setResendMessage(result.message ?? '');
    };

    const sessionEnded = mode === 'change' && state === 'success' && !isLoggedIn;

    return <Stack spacing={2} alignItems="center" sx={{ maxWidth: 520, mx: 'auto', textAlign: 'center' }}>
        <MarkEmailReadOutlinedIcon sx={{ fontSize: 56 }} color={state === 'error' ? 'disabled' : 'primary'} />
        <Typography variant="h4" component="h1">{title}</Typography>

        {state === 'loading' && (
            <Box display="flex" alignItems="center" gap={1.5}>
                <CircularProgress size={24} />
                <Typography color="text.secondary">Just a moment...</Typography>
            </Box>
        )}

        {state !== 'loading' && (
            <Alert severity={state === 'success' ? 'success' : 'error'} sx={{ width: '100%', textAlign: 'left' }}>
                {message}
                {sessionEnded && ' For your security you were logged out everywhere, please log in with your new email address.'}
            </Alert>
        )}

        {state === 'error' && mode === 'verify' && isLoggedIn && (
            <Stack spacing={1} alignItems="center">
                <Button variant="outlined" onClick={handleResend}>Send a new verification email</Button>
                {resendMessage && <Alert severity={resendSuccess ? 'success' : 'error'}>{resendMessage}</Alert>}
            </Stack>
        )}

        {state === 'error' && mode === 'change' && (
            <Typography variant="body2" color="text.secondary">
                You can request a new link by changing your email again in your account settings.
            </Typography>
        )}

        {state !== 'loading' && (
            sessionEnded || (!isLoggedIn && state === 'success')
                ? <Button variant="contained" onClick={() => navigate('/login')}>Log in</Button>
                : <Button onClick={() => navigate('/home')}>Go to the home page</Button>
        )}
    </Stack>
}
