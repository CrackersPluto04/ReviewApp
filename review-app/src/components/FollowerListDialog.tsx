import { useEffect, useState } from "preact/hooks";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { UserCompactDto } from "../types/types";
import { userService } from "../services/UserService";
import { followerService } from "../services/FollowerService";
import { Dialog, DialogTitle, Typography, IconButton, DialogContent, Box, CircularProgress, List, ListItem, ListItemAvatar, Avatar, ListItemText, Button } from "@mui/material";
import CloseIcon from '@mui/icons-material/Close';

type FollowerListDialogProps = {
    open: boolean;
    onClose: () => void;
    /** Whose followers or following are listed. */
    username: string;
    type: 'followers' | 'following';
    /** Called after the profile owner removed a follower, so the header count can drop. */
    onFollowerRemoved?: () => void;
    /** Called when the profile owner follows (+1) or unfollows (-1) someone from the list, so the header count follows. */
    onFollowingCountChanged?: (delta: number) => void;
}

/**
 * A dialog with a user's followers or following. Each row can be followed or unfollowed by the viewer, with an
 * optimistic update that rolls back if the request fails. On their own followers list the owner can also remove
 * a follower. The list is loaded every time the dialog opens, and clicking a user opens their profile.
 */
export function FollowerListDialog({ open, onClose, username, type, onFollowerRemoved, onFollowingCountChanged }: FollowerListDialogProps) {
    const { user, isLoggedIn } = useAuth();
    const navigate = useNavigate();

    const [users, setUsers] = useState<UserCompactDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    const isOwner = user?.username === username;

    useEffect(() => {
        if (!open) return;

        const fetchUsers = async () => {
            setLoading(true);

            const result = type === 'followers'
                ? await userService.getUserFollowers(username)
                : await userService.getUserFollowing(username);

            if (result.success)
                setUsers(result.data);
            else
                setErrorMessage(result.message);

            setLoading(false);
        };

        fetchUsers();
    }, [open, username, type]);

    const handleToggleFollow = async (targetUserId: number, currentlyFollowing: boolean) => {
        if (!isLoggedIn) return alert("You must be logged in to do this.");

        setUsers(prev => prev.map(u =>
            u.id === targetUserId ? { ...u, isFollowedByCurrentUser: !currentlyFollowing } : u
        ));

        if (isOwner && onFollowingCountChanged) {
            onFollowingCountChanged(currentlyFollowing ? -1 : 1);
        }

        const result = currentlyFollowing
            ? await followerService.unfollow(targetUserId)
            : await followerService.follow(targetUserId);

        if (!result.success) {
            setUsers(prev => prev.map(u =>
                u.id === targetUserId ? { ...u, isFollowedByCurrentUser: currentlyFollowing } : u
            ));

            if (isOwner && onFollowingCountChanged) {
                onFollowingCountChanged(currentlyFollowing ? 1 : -1);
            }

            setErrorMessage(result.message);
        }
    };

    const handleRemoveFollower = async (followerId: number) => {
        if (!globalThis.confirm("Remove this follower?")) return;

        setUsers(prev => prev.filter(u => u.id !== followerId));

        const result = await followerService.remove(followerId);
        if (result.success)
            onFollowerRemoved?.();
        else
            setErrorMessage(result.message);
    };

    const handleUserClick = (targetUsername: string) => {
        onClose();
        navigate(`/profile/${encodeURIComponent(targetUsername)}/overview`);
    };

    return <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
        <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="h6" fontWeight="bold">
                {type === 'followers' ? 'Followers' : 'Following'}
            </Typography>

            <IconButton onClick={onClose}>
                <CloseIcon />
            </IconButton>
        </DialogTitle>

        <DialogContent dividers sx={{ minHeight: 300 }}>
            {errorMessage && (
                <Typography color="error" variant="body2" sx={{ mb: 2 }}>
                    {errorMessage}
                </Typography>
            )}

            {loading ? (
                <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
            ) : users.length === 0 ? (
                // A failed load (e.g. an unknown user, 404) shows only its error, not an empty list
                !errorMessage && (
                    <Typography textAlign="center" color="text.secondary" mt={4}>
                        No {type} found.
                    </Typography>
                )
            ) : (
                <List>
                    {users.map((u) => (
                        <ListItem key={u.id} sx={{ px: 0 }}>
                            <ListItemAvatar sx={{ cursor: 'pointer' }} onClick={() => handleUserClick(u.username)}>
                                <Avatar src={u.profilePictureUrl}>
                                    {u.username.charAt(0).toUpperCase()}
                                </Avatar>
                            </ListItemAvatar>

                            <ListItemText
                                primary={
                                    <Typography fontWeight="bold" sx={{ cursor: 'pointer' }} onClick={() => handleUserClick(u.username)}>
                                        {u.username}
                                    </Typography>
                                }
                            />

                            {user?.username !== u.username && (
                                <Box sx={{ display: 'flex', gap: 1 }}>
                                    {isOwner && type === 'followers' && (
                                        <Button size="small" variant="outlined" color="error" onClick={() => handleRemoveFollower(u.id)}>
                                            Remove
                                        </Button>
                                    )}

                                    <Button
                                        size="small"
                                        variant={u.isFollowedByCurrentUser ? "outlined" : "contained"}
                                        onClick={() => handleToggleFollow(u.id, u.isFollowedByCurrentUser)}
                                    >
                                        {u.isFollowedByCurrentUser ? 'Unfollow' : 'Follow'}
                                    </Button>
                                </Box>
                            )}
                        </ListItem>
                    ))}
                </List>
            )}
        </DialogContent>
    </Dialog>
}