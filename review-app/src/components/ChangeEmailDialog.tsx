import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField, Typography } from "@mui/material";
import { useEffect, useState } from "preact/hooks";
import { isValidEmail, MAX_EMAIL_LENGTH } from "../utils/validation";

type ChangeEmailDialogProps = {
    open: boolean;
    onClose: () => void;
    /** Requests the change. The caller closes the dialog on success; a failure's message is shown here. */
    onSave: (newEmail: string, currentPassword: string) => Promise<{ success: boolean, message?: string }>;
};

/**
 * Asks for the new address (twice) and the current password. Nothing changes yet: the API mails a confirmation
 * link to the new address and the email changes once it is opened. The form, the password included, is cleared
 * whenever the dialog opens or closes, and the password is cleared after a failed attempt.
 */
export function ChangeEmailDialog({ open, onClose, onSave }: ChangeEmailDialogProps) {
    const [newEmail, setNewEmail] = useState('');
    const [confirmEmail, setConfirmEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        setNewEmail('');
        setConfirmEmail('');
        setPassword('');
        setError('');
        setSaving(false);
    }, [open]);

    const trimmedEmail = newEmail.trim();
    const isEmailInvalid = !isValidEmail(trimmedEmail);
    const isConfirmMismatch = trimmedEmail.toLowerCase() !== confirmEmail.trim().toLowerCase();
    const canSave = !isEmailInvalid && !isConfirmMismatch && password.length > 0 && !saving;

    const handleSave = async () => {
        if (!canSave) return;

        setSaving(true);
        setError('');

        const result = await onSave(trimmedEmail, password);
        if (!result.success) {
            setError(result.message || 'Failed to change email.');
            setPassword('');
            setSaving(false);
        }
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
        <DialogTitle>
            Change Email
        </DialogTitle>

        <DialogContent dividers>
            <Typography variant="body2" color="text.secondary" mb={2}>
                Enter your new email address and your current password to confirm it's you.
                We'll send a confirmation link to the new address, your email changes once you open it.
            </Typography>

            {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

            <TextField
                fullWidth autoFocus
                type="email"
                label="New email"
                margin="dense"
                autoComplete="off"
                value={newEmail}
                slotProps={{ htmlInput: { maxLength: MAX_EMAIL_LENGTH } }}
                onChange={(e: any) => { setNewEmail(e.target.value); setError(''); }}
                error={newEmail.length > 0 && isEmailInvalid}
                helperText={newEmail.length > 0 && isEmailInvalid ? 'Please enter a valid email address.' : ' '}
            />
            <TextField
                fullWidth
                type="email"
                label="Confirm new email"
                margin="dense"
                autoComplete="off"
                value={confirmEmail}
                slotProps={{ htmlInput: { maxLength: MAX_EMAIL_LENGTH } }}
                onChange={(e: any) => { setConfirmEmail(e.target.value); setError(''); }}
                error={confirmEmail.length > 0 && isConfirmMismatch}
                helperText={confirmEmail.length > 0 && isConfirmMismatch ? 'Emails do not match.' : ' '}
            />
            <TextField
                fullWidth
                type="password"
                label="Current password"
                margin="dense"
                autoComplete="current-password"
                value={password}
                onChange={(e: any) => { setPassword(e.target.value); setError(''); }}
                onKeyDown={(e: any) => { if (e.key === 'Enter') handleSave(); }}
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
