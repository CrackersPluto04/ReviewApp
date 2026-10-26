import { Button, Dialog, DialogActions, DialogContent, DialogTitle, useMediaQuery, useTheme } from "@mui/material";
import { useNavigate } from "react-router-dom";
import { SubscriptionPanel } from "./SubscriptionPanel";

type SubscriptionDialogProps = {
    open: boolean;
    onClose: () => void;
};

// Account settings > Subscription. The panel loads its status every time the dialog opens.
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
