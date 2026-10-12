import { useState } from "preact/hooks";
import { Alert, Button } from "@mui/material";
import { useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { securityService } from "../services/SecurityService";

const DISMISSED_KEY = 'emailVerificationBannerDismissed';

function readDismissed(): boolean {
    try {
        return sessionStorage.getItem(DISMISSED_KEY) === 'true';
    } catch {
        return false;
    }
}

// Non-blocking reminder for logged-in users with an unverified email, hidden for the rest of the session once closed
export function EmailVerificationBanner() {
    const { user } = useAuth();
    const location = useLocation();
    const [dismissed, setDismissed] = useState(readDismissed);
    const [sending, setSending] = useState(false);
    const [result, setResult] = useState<{ success: boolean, message: string }>();

    // Not shown on the page that is verifying it right now
    if (!user || user.isEmailConfirmed || dismissed || location.pathname === '/verify-email')
        return null;

    const handleDismiss = () => {
        setDismissed(true);
        try {
            sessionStorage.setItem(DISMISSED_KEY, 'true');
        } catch {
            // Storage can be unavailable (private mode), the banner just comes back on reload
        }
    };

    const handleResend = async () => {
        setSending(true);
        const response = await securityService.resendVerification();
        setResult({ success: response.success, message: response.message ?? '' });
        setSending(false);
    };

    if (result)
        return <Alert severity={result.success ? 'success' : 'error'} onClose={handleDismiss} sx={{ mb: 3 }}>
            {result.message}{result.success && " Don't forget to check your spam folder."}
        </Alert>;

    return <Alert
        severity="info"
        onClose={handleDismiss}
        sx={{ mb: 3, alignItems: 'center' }}
        action={
            <Button color="inherit" size="small" onClick={handleResend} disabled={sending}>
                Resend email
            </Button>
        }
    >
        Please verify your email address. We sent you a link when you registered (check your spam folder too).
    </Alert>
}
