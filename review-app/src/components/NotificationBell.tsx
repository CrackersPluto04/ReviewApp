import { useState } from "preact/hooks";
import { Badge, IconButton, Tooltip } from "@mui/material";
import NotificationsIcon from "@mui/icons-material/Notifications";
import NotificationsNoneIcon from "@mui/icons-material/NotificationsNone";
import { useNotifications } from "../context/NotificationContext";
import { NotificationsDialog } from "./NotificationsDialog";

export function NotificationBell() {
    const { unseenCount } = useNotifications();
    const [open, setOpen] = useState(false);

    const label = unseenCount === 0 ? 'Notifications' : `Notifications, ${unseenCount} unseen`;

    return <>
        <Tooltip title="Notifications">
            <IconButton color="inherit" aria-label={label} aria-haspopup="dialog" onClick={() => setOpen(true)}>
                <Badge badgeContent={unseenCount} max={99} color="error">
                    {unseenCount > 0 ? <NotificationsIcon /> : <NotificationsNoneIcon />}
                </Badge>
            </IconButton>
        </Tooltip>

        {/* Focus goes back to the bell when the dialog closes (MUI Dialog restores it) */}
        <NotificationsDialog open={open} onClose={() => setOpen(false)} />
    </>
}
