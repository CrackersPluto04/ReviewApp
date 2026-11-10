import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from "@mui/material";
import { useEffect, useState } from "preact/hooks";

type CancelSubscriptionDialogProps = {
    open: boolean;
    /** The already formatted date Premium runs until; empty when unknown (the text then says "the end of the period you paid for"). */
    accessEndsOn: string;
    onClose: () => void;
    /** Cancels the subscription. The caller closes the dialog on success; a failure's message is shown here. */
    onConfirm: () => Promise<{ success: boolean, message?: string }>;
};

/**
 * Confirmation before cancelling Premium. Cancelling only stops the renewal: access continues to the end of the
 * paid period and can be resumed until then, which the text says so users don't fear losing anything early.
 */
export function CancelSubscriptionDialog({ open, accessEndsOn, onClose, onConfirm }: CancelSubscriptionDialogProps) {
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState('');

    useEffect(() => {
        setIsSubmitting(false);
        setError('');
    }, [open]);

    const handleConfirm = async () => {
        setIsSubmitting(true);
        setError('');

        const result = await onConfirm();
        if (!result.success) {
            setError(result.message || 'Something went wrong. Please try again.');
            setIsSubmitting(false);
        }
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
        <DialogTitle>
            Cancel Premium?
        </DialogTitle>

        <DialogContent dividers>
            {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

            <Typography variant="body2" color="text.secondary">
                Your subscription won't renew. You keep every Premium feature
                {accessEndsOn ? ` until ${accessEndsOn}` : ' until the end of the period you paid for'}, and you can resume it until then.
                Nothing you created is deleted.
            </Typography>
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose} color="inherit">Keep Premium</Button>
            <Button onClick={handleConfirm} variant="contained" color="error" disabled={isSubmitting}>
                Cancel subscription
            </Button>
        </DialogActions>
    </Dialog>
}
