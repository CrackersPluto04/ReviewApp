import { Dialog, DialogTitle, DialogContent, Typography, TextField, DialogActions, Button, Box } from "@mui/material";
import { useEffect, useState } from "preact/hooks";
import { MAX_USERNAME_LENGTH, usernameError } from "../utils/validation";

type ChangeUsernameDialogProps = {
    open: boolean;
    onClose: () => void;
    currentUsername: string;
    onSave: (newUsername: string) => Promise<{ success: boolean, message?: string }>;
};

export function ChangeUsernameDialog({ open, onClose, currentUsername, onSave }: ChangeUsernameDialogProps) {
    const [newUsername, setNewUsername] = useState(currentUsername);
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);

    // Reset the draft every time the dialog is opened
    useEffect(() => {
        if (open) {
            setNewUsername(currentUsername);
            setError('');
            setSaving(false);
        }
    }, [open, currentUsername]);

    const trimmed = newUsername.trim();
    const ruleError = usernameError(trimmed);
    const isUnchanged = trimmed === currentUsername;

    const handleSave = async () => {
        if (ruleError || isUnchanged || saving) return;

        setSaving(true);
        setError('');

        const result = await onSave(trimmed);
        if (!result.success) {
            setError(result.message || 'Failed to change username.');
            setSaving(false);
        }
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
        <DialogTitle>
            Change Username
        </DialogTitle>

        <DialogContent dividers>
            <Typography variant="body2" color="text.secondary">
                Current username
            </Typography>
            <Typography variant="h6" fontWeight="bold" mb={3}>
                {currentUsername}
            </Typography>

            <TextField
                fullWidth
                autoFocus
                label="New username"
                value={newUsername}
                onChange={(e: any) => {
                    setNewUsername(e.target.value);
                    setError('');
                }}
                onKeyDown={(e: any) => { if (e.key === 'Enter') handleSave(); }}
                error={!!error || (newUsername.length > 0 && !!ruleError)}
                helperText={
                    <Box component="span" sx={{ display: 'flex', justifyContent: 'space-between' }}>
                        <span>{error || (newUsername.length > 0 ? ruleError : '')}</span>
                        <span>{trimmed.length} / {MAX_USERNAME_LENGTH}</span>
                    </Box>
                }
            />
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button onClick={handleSave} variant="contained" disabled={!!ruleError || isUnchanged || saving}>
                Save
            </Button>
        </DialogActions>
    </Dialog>
}
