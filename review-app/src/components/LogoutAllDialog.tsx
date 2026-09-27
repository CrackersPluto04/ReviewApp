import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from "@mui/material";
import { useEffect, useState } from "preact/hooks";

type LogoutAllDialogProps = {
    open: boolean;
    onClose: () => void;
    onConfirm: () => Promise<{ success: boolean, message?: string }>;
};

export function LogoutAllDialog({ open, onClose, onConfirm }: LogoutAllDialogProps) {
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
            Log out of all devices
        </DialogTitle>

        <DialogContent dividers>
            {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

            <Typography variant="body2" color="text.secondary">
                Every device where you are logged in, including this one, will be logged out.
                Use this if you think someone else has access to your account.
            </Typography>
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button onClick={handleConfirm} variant="contained" color="error" disabled={isSubmitting}>
                Log out everywhere
            </Button>
        </DialogActions>
    </Dialog>
}
