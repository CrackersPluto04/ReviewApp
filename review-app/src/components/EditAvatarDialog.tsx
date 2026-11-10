import { Dialog, DialogTitle, DialogContent, Typography, TextField, Box, Avatar, DialogActions, Button } from "@mui/material";
import { useState } from "preact/hooks";

type EditAvatarDialogProps = {
    open: boolean;
    onClose: () => void;
    /** The picture URL the field starts with; read once, when the component first renders. */
    currentAvatarUrl: string;
    /** Receives the URL as typed; the caller saves it and closes the dialog. */
    onSave: (newUrl: string) => void;
};

/** Sets the profile picture from a pasted image URL (pictures are not uploaded), with a live preview. */
export function EditAvatarDialog({ open, onClose, currentAvatarUrl = '', onSave }: EditAvatarDialogProps) {
    const [newAvatarUrl, setNewAvatarUrl] = useState(currentAvatarUrl);

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
        <DialogTitle>
            Update Profile Picture
        </DialogTitle>

        <DialogContent dividers>
            <Typography variant="body2" color="text.secondary" mb={2}>
                Paste a direct link (url) to an image.
            </Typography>
            <TextField
                fullWidth
                label="Image URL"
                value={newAvatarUrl}
                onChange={(e: any) => setNewAvatarUrl(e.target.value)}
            />
            {newAvatarUrl && (
                <Box mt={3} display="flex" justifyContent="center">
                    <Avatar src={newAvatarUrl} sx={{ width: 100, height: 100 }} />
                </Box>
            )}
        </DialogContent>

        <DialogActions>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button onClick={() => onSave(newAvatarUrl)} variant="contained">Save</Button>
        </DialogActions>
    </Dialog>
}