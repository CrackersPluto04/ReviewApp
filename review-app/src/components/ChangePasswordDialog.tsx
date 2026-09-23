import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField, Typography } from "@mui/material";
import { useEffect, useState } from "preact/hooks";
import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from "../utils/validation";

type ChangePasswordDialogProps = {
    open: boolean;
    onClose: () => void;
    onSave: (currentPassword: string, newPassword: string) => Promise<{ success: boolean, message?: string }>;
};

export function ChangePasswordDialog({ open, onClose, onSave }: ChangePasswordDialogProps) {
    const [currentPassword, setCurrentPassword] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);

    // Clear everything (all passwords) whenever the dialog is opened or closed
    useEffect(() => {
        setCurrentPassword('');
        setNewPassword('');
        setConfirmPassword('');
        setError('');
        setSaving(false);
    }, [open]);

    const isNewLengthInvalid = newPassword.length < MIN_PASSWORD_LENGTH || newPassword.length > MAX_PASSWORD_LENGTH;
    const isSameAsCurrent = newPassword.length > 0 && newPassword === currentPassword;
    const isConfirmMismatch = newPassword !== confirmPassword;
    const canSave = currentPassword.length > 0 && !isNewLengthInvalid && !isSameAsCurrent && !isConfirmMismatch && !saving;

    const newPasswordHelper = () => {
        if (newPassword.length === 0) return ' ';
        if (isNewLengthInvalid) return `Must be between ${MIN_PASSWORD_LENGTH} and ${MAX_PASSWORD_LENGTH} characters.`;
        if (isSameAsCurrent) return 'Must be different from the current password.';
        return ' ';
    };

    const handleSave = async () => {
        if (!canSave) return;

        setSaving(true);
        setError('');

        const result = await onSave(currentPassword, newPassword);
        if (!result.success) {
            setError(result.message || 'Failed to change password.');
            setCurrentPassword('');
            setSaving(false);
        }
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
        <DialogTitle>
            Change Password
        </DialogTitle>

        <DialogContent dividers>
            <Typography variant="body2" color="text.secondary" mb={2}>
                You will stay logged in on this device. Any other device will be logged out.
            </Typography>

            {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

            <TextField
                fullWidth autoFocus
                type="password"
                label="Current password"
                margin="dense"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(e: any) => { setCurrentPassword(e.target.value); setError(''); }}
                helperText=" "
            />
            <TextField
                fullWidth
                type="password"
                label="New password"
                margin="dense"
                autoComplete="new-password"
                value={newPassword}
                slotProps={{ htmlInput: { maxLength: MAX_PASSWORD_LENGTH } }}
                onChange={(e: any) => { setNewPassword(e.target.value); setError(''); }}
                error={newPassword.length > 0 && (isNewLengthInvalid || isSameAsCurrent)}
                helperText={newPasswordHelper()}
            />
            <TextField
                fullWidth
                type="password"
                label="Confirm new password"
                margin="dense"
                autoComplete="new-password"
                value={confirmPassword}
                slotProps={{ htmlInput: { maxLength: MAX_PASSWORD_LENGTH } }}
                onChange={(e: any) => { setConfirmPassword(e.target.value); setError(''); }}
                onKeyDown={(e: any) => { if (e.key === 'Enter') handleSave(); }}
                error={confirmPassword.length > 0 && isConfirmMismatch}
                helperText={confirmPassword.length > 0 && isConfirmMismatch ? 'Passwords do not match.' : ' '}
            />
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button onClick={handleSave} variant="contained" disabled={!canSave}>
                Save
            </Button>
        </DialogActions>
    </Dialog>
}
