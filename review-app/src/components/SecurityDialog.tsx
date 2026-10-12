import { useEffect, useState } from "preact/hooks";
import {
    Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, TextField, Typography
} from "@mui/material";
import VerifiedUserIcon from "@mui/icons-material/VerifiedUser";
import { SecurityStatusDto } from "../types/types";
import { securityService } from "../services/SecurityService";
import { useAuth } from "../context/AuthContext";

type SecurityDialogProps = {
    open: boolean;
    onClose: () => void;
};

// Enabling / disabling 2FA: password first (sends a code), then the code
type TwoFactorStep = 'idle' | 'password' | 'code';

export function SecurityDialog({ open, onClose }: SecurityDialogProps) {
    const { refreshUser } = useAuth();

    const [status, setStatus] = useState<SecurityStatusDto>();
    const [loading, setLoading] = useState(false);
    const [feedback, setFeedback] = useState<{ success: boolean, message: string }>();
    const [busy, setBusy] = useState(false);

    const [step, setStep] = useState<TwoFactorStep>('idle');
    const [password, setPassword] = useState('');
    const [code, setCode] = useState('');

    const loadStatus = async () => {
        setLoading(true);
        const result = await securityService.getStatus();
        if (result.success)
            setStatus(result.data);
        else
            setFeedback({ success: false, message: result.message ?? 'Something went wrong.' });
        setLoading(false);
    };

    // Everything (incl. the password) is cleared whenever the dialog is opened or closed
    useEffect(() => {
        setFeedback(undefined);
        setStep('idle');
        setPassword('');
        setCode('');
        setBusy(false);
        if (open) loadStatus();
    }, [open]);

    const enabling = !status?.isTwoFactorEnabled;

    const run = async (action: () => Promise<{ success: boolean, message?: string }>, onSuccess?: () => void | Promise<void>) => {
        setBusy(true);
        setFeedback(undefined);
        const result = await action();
        setFeedback({ success: result.success, message: result.message ?? (result.success ? 'Done.' : 'Something went wrong.') });
        if (result.success) await onSuccess?.();
        setBusy(false);
        return result.success;
    };

    const handleResendVerification = () => run(() => securityService.resendVerification());

    const handleCancelEmailChange = () => run(
        async () => {
            const result = await securityService.cancelEmailChange();
            return { ...result, message: result.success ? 'The pending email change was cancelled.' : result.message };
        },
        loadStatus
    );

    const handleRequestCode = () => run(
        () => securityService.requestTwoFactorChange(enabling, password),
        () => { setStep('code'); setCode(''); }
    );

    const handleConfirmCode = () => run(
        () => securityService.confirmTwoFactorChange(enabling, code),
        async () => {
            setStep('idle');
            setPassword('');
            setCode('');
            await loadStatus();
            await refreshUser();
        }
    );

    const handleCancelStep = () => {
        setStep('idle');
        setPassword('');
        setCode('');
        setFeedback(undefined);
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
        <DialogTitle>Security</DialogTitle>

        <DialogContent dividers>
            {feedback && (
                <Alert severity={feedback.success ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setFeedback(undefined)}>
                    {feedback.message}
                </Alert>
            )}

            {loading && !status && <Box display="flex" justifyContent="center" p={3}><CircularProgress /></Box>}

            {status && <Stack spacing={3}>
                {/* Email */}
                <Box>
                    <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
                        <Typography variant="subtitle1" fontWeight="bold">Email address</Typography>
                        <Chip size="small"
                            color={status.isEmailConfirmed ? 'success' : 'warning'}
                            label={status.isEmailConfirmed ? 'Verified' : 'Not verified'}
                        />
                    </Stack>
                    <Typography variant="body2" sx={{ wordBreak: 'break-all' }}>{status.email}</Typography>

                    {!status.isEmailConfirmed && (
                        <Box sx={{ mt: 1 }}>
                            <Typography variant="body2" color="text.secondary">
                                Verify your address to enable two-factor authentication and to change your email.
                            </Typography>
                            <Button size="small" sx={{ mt: 0.5 }} onClick={handleResendVerification} disabled={busy}>
                                Send verification email
                            </Button>
                        </Box>
                    )}

                    {status.pendingEmail && (
                        <Alert severity="info" sx={{ mt: 1.5 }}
                            action={<Button color="inherit" size="small" onClick={handleCancelEmailChange} disabled={busy}>Cancel change</Button>}
                        >
                            Change to <strong>{status.pendingEmail}</strong> is waiting for confirmation. Open the link we sent to that address.
                        </Alert>
                    )}
                </Box>

                <Divider />

                {/* Two-factor authentication */}
                <Box>
                    <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
                        <VerifiedUserIcon fontSize="small" color={status.isTwoFactorEnabled ? 'success' : 'disabled'} />
                        <Typography variant="subtitle1" fontWeight="bold">Two-factor authentication</Typography>
                        <Chip size="small" color={status.isTwoFactorEnabled ? 'success' : 'default'} label={status.isTwoFactorEnabled ? 'On' : 'Off'} />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                        When it's on, logging in also requires a 6-digit code that we send to your email address.
                    </Typography>

                    {step === 'idle' && (
                        <Button variant={enabling ? 'contained' : 'outlined'} color={enabling ? 'primary' : 'error'} sx={{ mt: 1.5 }}
                            disabled={enabling && !status.isEmailConfirmed}
                            onClick={() => { setFeedback(undefined); setStep('password'); }}
                        >
                            {enabling ? 'Enable two-factor authentication' : 'Disable two-factor authentication'}
                        </Button>
                    )}

                    {step === 'idle' && enabling && !status.isEmailConfirmed && (
                        <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                            Verify your email address first.
                        </Typography>
                    )}

                    {step !== 'idle' && enabling && (
                        <Alert severity="warning" sx={{ mt: 1.5 }}>
                            If you lose access to your email address, you won't be able to log in. Recovery codes aren't available yet.
                        </Alert>
                    )}

                    {step === 'password' && (
                        <Stack spacing={1} sx={{ mt: 1.5 }}>
                            <Typography variant="body2">Enter your current password, we'll email you a code.</Typography>
                            <TextField
                                fullWidth autoFocus
                                type="password"
                                label="Current password"
                                autoComplete="current-password"
                                value={password}
                                onChange={(e: any) => setPassword(e.target.value)}
                                onKeyDown={(e: any) => { if (e.key === 'Enter' && password) handleRequestCode(); }}
                            />
                            <Stack direction="row" spacing={1} justifyContent="flex-end">
                                <Button color="inherit" onClick={handleCancelStep}>Cancel</Button>
                                <Button variant="contained" onClick={handleRequestCode} disabled={!password || busy}>Send code</Button>
                            </Stack>
                        </Stack>
                    )}

                    {step === 'code' && (
                        <Stack spacing={1} sx={{ mt: 1.5 }}>
                            <Typography variant="body2">Enter the 6-digit code from the email. It expires in 10 minutes.</Typography>
                            <TextField
                                fullWidth autoFocus
                                label="Code"
                                value={code}
                                onChange={(e: any) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                                onKeyDown={(e: any) => { if (e.key === 'Enter' && code.length === 6) handleConfirmCode(); }}
                                slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code', maxLength: 6, style: { letterSpacing: 6 } } }}
                            />
                            <Stack direction="row" spacing={1} justifyContent="flex-end">
                                <Button color="inherit" onClick={handleCancelStep}>Cancel</Button>
                                <Button onClick={handleRequestCode} disabled={busy}>Resend code</Button>
                                <Button variant="contained" color={enabling ? 'primary' : 'error'} onClick={handleConfirmCode} disabled={code.length !== 6 || busy}>
                                    {enabling ? 'Enable' : 'Disable'}
                                </Button>
                            </Stack>
                        </Stack>
                    )}
                </Box>
            </Stack>}
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose}>Close</Button>
        </DialogActions>
    </Dialog>
}
