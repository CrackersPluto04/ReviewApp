import { Avatar, Box, Button, Card, IconButton, Stack, Tooltip, Typography } from "@mui/material";
import ArrowForwardIcon from "@mui/icons-material/ArrowForward";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import MarkEmailReadOutlinedIcon from "@mui/icons-material/MarkEmailReadOutlined";
import MarkEmailUnreadOutlinedIcon from "@mui/icons-material/MarkEmailUnreadOutlined";
import EmojiEventsIcon from "@mui/icons-material/EmojiEvents";
import ChatBubbleOutlineIcon from "@mui/icons-material/ChatBubbleOutline";
import PersonAddAlt1Icon from "@mui/icons-material/PersonAddAlt1";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { NotificationDto } from "../types/types";
import { formatNotificationDate, NOTIFICATION_TYPE_LABELS } from "../utils/notifications";

type NotificationCardProps = {
    notification: NotificationDto;
    hasSource: boolean;
    onToggleSeen: (notification: NotificationDto) => void;
    onDelete: (notification: NotificationDto) => void;
    onOpen: (notification: NotificationDto) => void;
};

function TypeIcon({ notification }: { notification: NotificationDto }) {
    // Actor avatar for notifications triggered by a person, a type icon otherwise
    if (notification.actorUsername)
        return <Avatar src={notification.actorProfilePictureUrl ?? undefined} alt={notification.actorUsername} sx={{ width: 40, height: 40 }} />;

    const icon = {
        AchievementUnlocked: <EmojiEventsIcon />,
        ReviewReply: <ChatBubbleOutlineIcon />,
        ReplyReply: <ChatBubbleOutlineIcon />,
        NewFollower: <PersonAddAlt1Icon />,
        System: <InfoOutlinedIcon />
    }[notification.type] ?? <InfoOutlinedIcon />;

    return <Avatar sx={{ width: 40, height: 40, bgcolor: 'action.selected', color: 'text.primary' }}>{icon}</Avatar>;
}

export function NotificationCard({ notification, hasSource, onToggleSeen, onDelete, onOpen }: NotificationCardProps) {
    const unseen = !notification.isSeen;

    return <Card
        variant="outlined"
        component="li"
        aria-label={`${unseen ? 'Unseen' : 'Seen'} notification: ${notification.title}`}
        sx={{
            listStyle: 'none',
            p: 1.5,
            borderRadius: 2,
            display: 'flex',
            gap: 1.5,
            // Unseen ones stand out with an accent edge and a tinted background
            borderLeft: '4px solid',
            borderLeftColor: unseen ? 'primary.main' : 'divider',
            bgcolor: unseen ? 'action.hover' : 'background.paper'
        }}
    >
        <TypeIcon notification={notification} />

        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Stack direction="row" alignItems="flex-start" spacing={1}>
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography variant="subtitle2" fontWeight={unseen ? 'bold' : 'medium'} sx={{ wordBreak: 'break-word' }}>
                        {notification.title}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                        {NOTIFICATION_TYPE_LABELS[notification.type] ?? notification.type} · {formatNotificationDate(notification.createdAt)}
                    </Typography>
                </Box>

                <Tooltip title={unseen ? 'Mark as seen' : 'Mark as unseen'}>
                    <IconButton size="small" aria-label={unseen ? 'Mark as seen' : 'Mark as unseen'} onClick={() => onToggleSeen(notification)}>
                        {unseen ? <MarkEmailReadOutlinedIcon fontSize="small" /> : <MarkEmailUnreadOutlinedIcon fontSize="small" />}
                    </IconButton>
                </Tooltip>
                <Tooltip title="Delete">
                    <IconButton size="small" aria-label="Delete notification" onClick={() => onDelete(notification)}>
                        <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                </Tooltip>
            </Stack>

            {/* Plain text only, messages contain usernames and reply snippets */}
            <Typography variant="body2" sx={{ mt: 0.5, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                {notification.message}
            </Typography>

            {hasSource && (
                <Box sx={{ display: 'flex', justifyContent: 'flex-end', flexGrow: 1, mt: 0.5 }}>
                    <Button size="small" endIcon={<ArrowForwardIcon />} onClick={() => onOpen(notification)}>
                        Take me there
                    </Button>
                </Box>
            )}
        </Box>
    </Card>
}
