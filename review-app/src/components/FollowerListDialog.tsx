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
    username: string;
    type: 'followers' | 'following';
    onFollowerRemoved?: () => void;
    onFollowingCountChanged?: (delta: number) => void;
}

export function FollowerListDialog({ open, onClose, username, type, onFollowerRemoved, onFollowingCountChanged }: FollowerListDialogProps) {
    const { user, isLoggedIn } = useAuth();
    const navigate = useNavigate();

    const [users, setUsers] = useState<UserCompactDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    const isOwner = user?.username === username;

    // Fetch requested list
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
        navigate(`/profile/${targetUsername}/overview`);
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
                <Typography textAlign="center" color="text.secondary" mt={4}>
                    No {type} found.
                </Typography>
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

                            {/* ACTION BUTTONS */}
                            {user?.username !== u.username && (
                                <Box sx={{ display: 'flex', gap: 1 }}>
                                    {/* Remove Button (Only for Owner looking at Followers) */}
                                    {isOwner && type === 'followers' && (
                                        <Button size="small" variant="outlined" color="error" onClick={() => handleRemoveFollower(u.id)}>
                                            Remove
                                        </Button>
                                    )}

                                    {/* Follow/Unfollow Button */}
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