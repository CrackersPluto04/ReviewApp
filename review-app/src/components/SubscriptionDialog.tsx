import { Button, Dialog, DialogActions, DialogContent, DialogTitle, useMediaQuery, useTheme } from "@mui/material";
import { useNavigate } from "react-router-dom";
import { SubscriptionPanel } from "./SubscriptionPanel";

type SubscriptionDialogProps = {
    open: boolean;
    onClose: () => void;
};

/**
 * Account settings > Subscription: the `SubscriptionPanel` in a dialog (full screen on small screens), with a
 * shortcut to the plans page. The panel is only mounted while the dialog is open, so it loads a fresh status
 * every time.
 */
export function SubscriptionDialog({ open, onClose }: SubscriptionDialogProps) {
    const navigate = useNavigate();
    const theme = useTheme();
    const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen}>
        <DialogTitle>Subscription</DialogTitle>

        <DialogContent dividers>
            {open && <SubscriptionPanel />}
        </DialogContent>

        <DialogActions>
            <Button onClick={() => { onClose(); navigate('/premium'); }}>See plans</Button>
            <Button onClick={onClose}>Close</Button>
        </DialogActions>
    </Dialog>
}
