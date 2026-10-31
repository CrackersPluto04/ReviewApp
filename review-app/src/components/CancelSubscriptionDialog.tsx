import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from "@mui/material";
import { useEffect, useState } from "preact/hooks";

type CancelSubscriptionDialogProps = {
    open: boolean;
    accessEndsOn: string; // formatted date
    onClose: () => void;
    onConfirm: () => Promise<{ success: boolean, message?: string }>;
};

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
