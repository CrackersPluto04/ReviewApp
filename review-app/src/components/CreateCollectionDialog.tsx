import { useState } from "preact/hooks";
import { collectionService } from "../services/CollectionService";
import { Dialog, DialogTitle, DialogContent, Typography, TextField, FormControl, InputLabel, Select, MenuItem, DialogActions, Button } from "@mui/material";

type CreateCollectionDialogProps = {
    open: boolean;
    onClose: () => void;
    onSuccess: (newCollectionId: number) => void;
}

export function CreateCollectionDialog({ open, onClose, onSuccess }: CreateCollectionDialogProps) {
    const [name, setName] = useState('');
    const [visibilityLevel, setVisibilityLevel] = useState(0); // 0 = Private - default
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string>('');

    const handleResetAndClose = () => {
        setName('');
        setVisibilityLevel(0);
        setErrorMessage('');
        onClose();
    };

    const handleCreate = async () => {
        if (!name.trim()) {
            setErrorMessage("Collection name cannot be empty.");
            return;
        }

        setLoading(true);
        setErrorMessage('');

        const result = await collectionService.createCollection(name.trim(), visibilityLevel);
        if (result.success) {
            onSuccess(result.data.id);
            handleResetAndClose();
        } else {
            setErrorMessage(result.message || "Failed to create collection.");
        }

        setLoading(false);
    };

    return <Dialog open={open} onClose={loading ? undefined : handleResetAndClose} fullWidth maxWidth="sm">
        <DialogTitle fontWeight="bold">
            Create New Collection
        </DialogTitle>

        <DialogContent dividers>
            {errorMessage && (
                <Typography color="error" variant="body2" sx={{ mb: 2 }}>
                    {errorMessage}
                </Typography>
            )}

            <TextField
                autoFocus margin="dense"
                fullWidth variant="outlined"
                label="Collection Name"
                value={name}
                onChange={(e) => setName(e.currentTarget.value)}
                disabled={loading}
                sx={{ mb: 3, mt: 1 }}
            />

            <FormControl fullWidth disabled={loading}>
                <InputLabel>Visibility</InputLabel>

                <Select
                    value={visibilityLevel}
                    label="Visibility"
                    onChange={(e) => setVisibilityLevel(Number((e.target as HTMLInputElement).value))}
                >
                    <MenuItem value={0}>Private</MenuItem>
                    <MenuItem value={1}>Public</MenuItem>
                    <MenuItem value={2}>Followers Only</MenuItem>
                </Select>
            </FormControl>
        </DialogContent>

        <DialogActions sx={{ p: 2 }}>
            <Button onClick={handleResetAndClose} color="inherit" disabled={loading}>
                Cancel
            </Button>

            <Button
                onClick={handleCreate}
                variant="contained"
                color="primary"
                disabled={loading || !name.trim()}
            >
                {loading ? 'Creating...' : 'Create'}
            </Button>
        </DialogActions>
    </Dialog>
}