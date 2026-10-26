import { useEffect, useState } from "preact/hooks";
import { Alert, Box, Button, Chip, CircularProgress, Stack, Typography } from "@mui/material";
import { SubscriptionStatusDto } from "../types/types";
import { subscriptionService } from "../services/SubscriptionService";
import { useAuth } from "../context/AuthContext";
import { formatSubscriptionDate } from "../utils/subscription";
import { PremiumBadge } from "./PremiumBadge";
import { PremiumUpsell } from "./PremiumUpsell";
import { CancelSubscriptionDialog } from "./CancelSubscriptionDialog";

type ActionResult = { success: boolean, message?: string, data?: any };

// The current user's subscription with its actions (account settings dialog and the plans page)
export function SubscriptionPanel() {
    const { refreshUser } = useAuth();

    const [status, setStatus] = useState<SubscriptionStatusDto>();
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ success: boolean, message: string }>();
    const [busy, setBusy] = useState(false);
    const [confirmCancel, setConfirmCancel] = useState(false);

    const loadStatus = async () => {
        setLoading(true);
        const result = await subscriptionService.getStatus();
        if (result.success)
            setStatus(result.data);
        else
            setFeedback({ success: false, message: result.message ?? 'Something went wrong.' });
        setLoading(false);
    };

    useEffect(() => {
        void loadStatus();
    }, []);

    // Cancel / resume: reload the status and the user (tier, entitlements) afterwards
    const run = async (action: () => Promise<ActionResult>) => {
        setBusy(true);
        setFeedback(undefined);
        const result = await action();
        setFeedback({ success: result.success, message: result.message ?? (result.success ? 'Done.' : 'Something went wrong.') });
        if (result.success) {
            await loadStatus();
            await refreshUser();
        }
        setBusy(false);
        return result;
    };

    // The payment settings live on the provider's page
    const handleManagePayment = async () => {
        setBusy(true);
        setFeedback(undefined);
        const result = await subscriptionService.openPortal();
        if (result.success && result.data?.url) {
            globalThis.location.href = result.data.url;
            return;
        }
        setFeedback({ success: false, message: result.message ?? 'Something went wrong.' });
        setBusy(false);
    };

    const handleCancel = async () => {
        const result = await run(() => subscriptionService.cancel());
        if (result.success) setConfirmCancel(false);
        return result;
    };

    if (loading && !status)
        return <Box display="flex" justifyContent="center" p={3}><CircularProgress /></Box>;

    const accessEndsOn = formatSubscriptionDate(status?.accessEndsAt);

    return <Stack spacing={2}>
        {feedback && (
            <Alert severity={feedback.success ? 'success' : 'error'} onClose={() => setFeedback(undefined)}>
                {feedback.message}
            </Alert>
        )}

        {status && !status.isPremium && (
            <PremiumUpsell
                title="You're on the Free plan"
                message={status.status === 'Ended'
                    ? 'Your Premium subscription has ended. You can subscribe again any time.'
                    : 'Subscribe to Premium to unlock exclusive features.'}
            />
        )}

        {status?.isPremium && <>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <PremiumBadge />
                {status.willRenew && <Chip size="small" color="success" label="Renews automatically" />}
                {status.cancelAtPeriodEnd && <Chip size="small" color="default" label="Cancelled" />}
                {status.hasPaymentProblem && <Chip size="small" color="warning" label="Payment problem" />}
            </Stack>

            {status.hasPaymentProblem ? (
                <Alert severity="warning">
                    We couldn't renew your subscription. Update your payment details
                    {accessEndsOn ? ` by ${accessEndsOn}` : ''} to keep Premium.
                </Alert>
            ) : (
                // Dates are locale formatted and may end with a dot, so they never end a sentence here
                <Box>
                    <Typography variant="body2">
                        {status.cancelAtPeriodEnd ? 'Premium until: ' : 'Next renewal: '}<strong>{accessEndsOn}</strong>
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                        {status.cancelAtPeriodEnd
                            ? "Your subscription won't renew. You can resume it until then."
                            : 'You can cancel any time.'}
                    </Typography>
                </Box>
            )}

            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {status.cancelAtPeriodEnd && (
                    <Button variant="contained" onClick={() => run(() => subscriptionService.resume())} disabled={busy}>
                        Resume subscription
                    </Button>
                )}
                <Button variant={status.hasPaymentProblem ? 'contained' : 'outlined'} onClick={handleManagePayment} disabled={busy}>
                    Manage payment details
                </Button>
                {!status.cancelAtPeriodEnd && (
                    <Button color="error" onClick={() => setConfirmCancel(true)} disabled={busy}>
                        Cancel subscription
                    </Button>
                )}
            </Stack>
        </>}

        <CancelSubscriptionDialog
            open={confirmCancel}
            accessEndsOn={accessEndsOn}
            onClose={() => setConfirmCancel(false)}
            onConfirm={handleCancel}
        />
    </Stack>
}
