import { useEffect, useState } from "preact/hooks";
import { collectionService } from "../services/CollectionService";
import { Dialog, DialogTitle, DialogContent, Typography, TextField, FormControl, InputLabel, Select, MenuItem, DialogActions, Button, Box } from "@mui/material";
import { CollectionDto } from "../types/types";

type CreateEditCollectionDialogProps = {
    open: boolean;
    onClose: () => void;
    onSuccess: ((newCollectionId: number) => void) | ((updatedColl: CollectionDto) => void);
    collection?: CollectionDto | null;
}

export function CreateEditCollectionDialog({ open, onClose, onSuccess, collection = null }: CreateEditCollectionDialogProps) {
    const [name, setName] = useState('');
    const [visibilityLevel, setVisibilityLevel] = useState(0);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string>('');

    const isEditing = !!collection;
    const isNameOver = name.length > 50;

    useEffect(() => {
        if (open) {
            if (isEditing) {
                setName(collection.name);
                setVisibilityLevel(collection.visibilityLevel)
            } else {
                setName('');
                setVisibilityLevel(0);
            }

            setErrorMessage('');
        }
    }, [open, collection])

    const handleResetAndClose = () => {
        setName('');
        setVisibilityLevel(0);
        setErrorMessage('');
        onClose();
    };

    const handleSubmit = async () => {
        if (!name.trim()) {
            setErrorMessage("Collection name cannot be empty.");
            return;
        }

        setLoading(true);
        setErrorMessage('');

        let result;
        if (isEditing)
            result = await collectionService.updateCollection(collection.id, name.trim(), visibilityLevel);
        else
            result = await collectionService.createCollection(name.trim(), visibilityLevel);

        if (result.success) {
            isEditing ? onSuccess(result.data) : onSuccess(result.data.id);
            handleResetAndClose();
        } else {
            setErrorMessage(result.message || `Failed to ${isEditing ? 'edit' : 'create'} collection.`);
        }

        setLoading(false);
    };

    return <Dialog open={open} onClose={loading ? undefined : handleResetAndClose} fullWidth maxWidth="sm">
        <DialogTitle fontWeight="bold">
            {isEditing ? 'Edit Collection' : 'Create New Collection'}
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
                error={isNameOver}
                helperText={<Box component="span" sx={{ float: 'right' }}>{name.length} / 50</Box>}
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
                onClick={handleSubmit}
                variant="contained"
                color="primary"
                disabled={loading || !name.trim() || isNameOver}
            >
                {loading ? (isEditing ? 'Saving...' : 'Creating...') : (isEditing ? 'Save Changes' : 'Create')}
            </Button>
        </DialogActions>
    </Dialog>
}