import { useEffect, useRef, useState } from "preact/hooks";
import {
    Alert, Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, IconButton, InputLabel,
    MenuItem, Select, Stack, ToggleButton, ToggleButtonGroup, Tooltip, Typography, useMediaQuery, useTheme
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import DoneAllIcon from "@mui/icons-material/DoneAll";
import DeleteSweepOutlinedIcon from "@mui/icons-material/DeleteSweepOutlined";
import { useNavigate } from "react-router-dom";
import { NotificationDto, NotificationSort, NotificationStatusFilter } from "../types/types";
import { notificationService } from "../services/NotificationService";
import { useNotifications } from "../context/NotificationContext";
import { useAuth } from "../context/AuthContext";
import { getNotificationRoute } from "../utils/notifications";
import { NotificationCard } from "./NotificationCard";

const PAGE_SIZE = 20;

type NotificationsDialogProps = {
    open: boolean;
    onClose: () => void;
};

// Both or neither toggle selected means every notification
function toStatus(toggles: string[]): NotificationStatusFilter {
    if (toggles.length === 1) return toggles[0] as NotificationStatusFilter;
    return 'all';
}

export function NotificationsDialog({ open, onClose }: NotificationsDialogProps) {
    const navigate = useNavigate();
    const theme = useTheme();
    const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
    const { user } = useAuth();
    const { unseenCount, adjustUnseenCount, setUnseenCount } = useNotifications();

    const [toggles, setToggles] = useState<string[]>([]);
    const [sort, setSort] = useState<NotificationSort>('newest');
    const status = toStatus(toggles);

    const [items, setItems] = useState<NotificationDto[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [hasMore, setHasMore] = useState(false);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');
    const [confirmClear, setConfirmClear] = useState(false);

    // Cursor = last ID received from the server; responses of an outdated filter are dropped
    const cursorRef = useRef<number | undefined>(undefined);
    const requestIdRef = useRef(0);

    const load = async (reset: boolean) => {
        const requestId = ++requestIdRef.current;
        setLoading(true);
        setErrorMessage('');

        const result = await notificationService.getNotifications({
            status,
            sort,
            afterId: reset ? undefined : cursorRef.current,
            pageSize: PAGE_SIZE
        });
        if (requestId !== requestIdRef.current) return;

        if (result.success) {
            const page = result.data;
            setItems(prev => reset ? page.items : [...prev, ...page.items.filter((n: NotificationDto) => !prev.some(p => p.id === n.id))]);
            if (page.items.length > 0) cursorRef.current = page.items[page.items.length - 1].id;
            else if (reset) cursorRef.current = undefined;
            setHasMore(page.hasMore);
            setTotalCount(page.totalCount);
            setUnseenCount(page.unseenCount);
        } else
            setErrorMessage(result.message ?? 'Something went wrong.');

        setLoading(false);
    };

    // (Re)load whenever the dialog opens or the filter changes
    useEffect(() => {
        if (!open) return;
        cursorRef.current = undefined;
        load(true);
    }, [open, status, sort]);

    useEffect(() => {
        if (!open) setConfirmClear(false);
    }, [open]);

    /* Actions: applied locally right away, rolled back if the request fails */

    const handleToggleSeen = async (notification: NotificationDto) => {
        const isSeen = !notification.isSeen;
        setItems(prev => prev.map(n => n.id === notification.id ? { ...n, isSeen } : n));
        adjustUnseenCount(isSeen ? -1 : 1);

        const result = await notificationService.setSeen(notification.id, isSeen);
        if (!result.success) {
            setItems(prev => prev.map(n => n.id === notification.id ? { ...n, isSeen: notification.isSeen } : n));
            adjustUnseenCount(isSeen ? 1 : -1);
            setErrorMessage(result.message ?? 'Something went wrong.');
        }
    };

    const handleDelete = async (notification: NotificationDto) => {
        const index = items.findIndex(n => n.id === notification.id);
        setItems(prev => prev.filter(n => n.id !== notification.id));
        setTotalCount(prev => Math.max(0, prev - 1));
        if (!notification.isSeen) adjustUnseenCount(-1);

        const result = await notificationService.deleteNotification(notification.id);
        if (!result.success) {
            setItems(prev => {
                const restored = [...prev];
                restored.splice(Math.min(index, restored.length), 0, notification);
                return restored;
            });
            setTotalCount(prev => prev + 1);
            if (!notification.isSeen) adjustUnseenCount(1);
            setErrorMessage(result.message ?? 'Something went wrong.');
        }
    };

    const handleOpen = (notification: NotificationDto) => {
        const route = getNotificationRoute(notification, user?.username);
        if (!route) return;

        // Following a link counts as seeing it (fire and forget, the badge is adjusted right away)
        if (!notification.isSeen) {
            adjustUnseenCount(-1);
            notificationService.setSeen(notification.id, true);
        }

        onClose();
        navigate(route);
    };

    const handleMarkAllSeen = async () => {
        const previous = items;
        const previousCount = unseenCount;
        setItems(prev => prev.map(n => ({ ...n, isSeen: true })));
        setUnseenCount(0);

        const result = await notificationService.markAllSeen();
        if (!result.success) {
            setItems(previous);
            setUnseenCount(previousCount);
            setErrorMessage(result.message ?? 'Something went wrong.');
        } else if (status !== 'all')
            load(true);
    };

    const handleClearSeen = async () => {
        setConfirmClear(false);
        const result = await notificationService.deleteSeen();
        if (result.success) {
            cursorRef.current = undefined;
            load(true);
        } else
            setErrorMessage(result.message ?? 'Something went wrong.');
    };

    const isEmpty = !loading && items.length === 0 && !errorMessage;

    return <Dialog open={open} onClose={onClose} fullScreen={fullScreen} fullWidth maxWidth="sm" aria-labelledby="notifications-title"
        slotProps={{ paper: { sx: { height: fullScreen ? '100%' : '80vh' } } }}
    >
        <DialogTitle id="notifications-title" sx={{ pb: 1 }}>
            <Stack direction="row" alignItems="center" spacing={1}>
                <Box sx={{ flexGrow: 1 }}>
                    Notifications ({totalCount})
                    <Typography variant="body2" color="text.secondary" aria-live="polite">
                        {unseenCount === 0 ? 'You are all caught up' : `${unseenCount} unseen`}
                    </Typography>
                </Box>
                <IconButton aria-label="Close notifications" onClick={onClose}>
                    <CloseIcon />
                </IconButton>
            </Stack>

            {/* Filters and bulk actions, they stay in place while the list scrolls */}
            <Stack direction="row" alignItems="center" flexWrap="wrap" useFlexGap spacing={1} sx={{ mt: 1.5 }}>
                <ToggleButtonGroup size="small" value={toggles} onChange={(_e: any, value: string[]) => setToggles(value)} aria-label="Filter by seen state">
                    <ToggleButton value="seen">Seen</ToggleButton>
                    <ToggleButton value="unseen">Unseen</ToggleButton>
                </ToggleButtonGroup>

                <FormControl size="small" sx={{ minWidth: 130 }}>
                    <InputLabel id="notification-sort-label">Sort by</InputLabel>
                    <Select labelId="notification-sort-label" label="Sort by" value={sort}
                        onChange={(e: any) => setSort(e.target.value as NotificationSort)}
                    >
                        <MenuItem value="newest">Newest first</MenuItem>
                        <MenuItem value="oldest">Oldest first</MenuItem>
                    </Select>
                </FormControl>

                {/* Kept together so they wrap as one group on narrow screens */}
                <Stack direction="row" sx={{ ml: 'auto' }}>
                    <Tooltip title="Mark all as seen">
                        <span>
                            <IconButton aria-label="Mark all as seen" onClick={handleMarkAllSeen} disabled={unseenCount === 0}>
                                <DoneAllIcon />
                            </IconButton>
                        </span>
                    </Tooltip>
                    <Tooltip title="Delete all seen">
                        <IconButton aria-label="Delete all seen notifications" onClick={() => setConfirmClear(true)}>
                            <DeleteSweepOutlinedIcon />
                        </IconButton>
                    </Tooltip>
                </Stack>
            </Stack>

            {confirmClear && (
                <Alert severity="warning" sx={{ mt: 1.5 }}
                    action={<>
                        <Button color="inherit" size="small" onClick={() => setConfirmClear(false)}>Cancel</Button>
                        <Button color="inherit" size="small" onClick={handleClearSeen}>Delete</Button>
                    </>}
                >
                    Delete every seen notification? This cannot be undone.
                </Alert>
            )}
        </DialogTitle>

        <DialogContent dividers>
            {errorMessage && <Alert severity="error" sx={{ mb: 2 }} onClose={() => setErrorMessage('')}>{errorMessage}</Alert>}

            {isEmpty && (
                <Box sx={{ p: 5, textAlign: 'center', color: 'text.secondary' }}>
                    <Typography>
                        {status === 'all' ? 'You have no notifications yet.' : `No ${status} notifications.`}
                    </Typography>
                </Box>
            )}

            <Stack component="ul" spacing={1.5} sx={{ p: 0, m: 0 }}>
                {items.map(notification => (
                    <NotificationCard
                        key={notification.id}
                        notification={notification}
                        hasSource={getNotificationRoute(notification, user?.username) !== null}
                        onToggleSeen={handleToggleSeen}
                        onDelete={handleDelete}
                        onOpen={handleOpen}
                    />
                ))}
            </Stack>

            {loading && <Box display="flex" justifyContent="center" py={2}><CircularProgress size={28} /></Box>}

            {hasMore && !loading && (
                <Box display="flex" justifyContent="center" pt={2}>
                    <Button onClick={() => load(false)}>Load more</Button>
                </Box>
            )}
        </DialogContent>

        {fullScreen && (
            <DialogActions>
                <Button onClick={onClose}>Close</Button>
            </DialogActions>
        )}
    </Dialog>
}
