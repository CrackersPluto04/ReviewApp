import { Box, Avatar, Typography, Paper, Tabs, Tab, CircularProgress, Button, IconButton, TextField, Badge } from "@mui/material";
import EditIcon from '@mui/icons-material/Edit';
import { useEffect, useState } from "preact/hooks";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { UserProfileDto } from "../types/types";
import { useAuth } from "../context/AuthContext";
import { userService } from "../services/UserService";
import { followerService } from "../services/FollowerService";
import { FollowerListDialog } from "../components/FollowerListDialog";
import { EditAvatarDialog } from "../components/EditAvatarDialog";

export function ProfileLayout() {
    const { user, setUser, isLoggedIn } = useAuth();
    const { username } = useParams();
    const navigate = useNavigate();
    const location = useLocation();

    const [profileData, setProfileData] = useState<UserProfileDto>();
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');
    const isOwner = user?.username === username;

    const [dialogOpen, setDialogOpen] = useState(false);
    const [dialogType, setDialogType] = useState<'followers' | 'following'>('followers');
    const [editAvatarDialogOpen, setEditAvatarDialogOpen] = useState(false);
    const [isEditingBio, setIsEditingBio] = useState(false);
    const [draftBio, setDraftBio] = useState(profileData?.bio || '');

    const currentTab = location.pathname.split('/').pop() || 'collections';
    const isBioOver = draftBio.length > 150;

    // Fetch user profile informations on load
    useEffect(() => {
        const fetchProfile = async () => {
            if (!username) return;

            setLoading(true);
            setErrorMessage('');

            const result = await userService.getUserProfile(username);
            if (result.success)
                setProfileData(result.data);
            else
                setErrorMessage(result.message);

            setLoading(false);
        }

        fetchProfile();
    }, [username, user]);

    // -- Handlers --
    const handleTabChange = (_event: any, newValue: string) => {
        navigate(`/profile/${username}/${newValue}`);
    };

    const handleOpenDialog = (type: 'followers' | 'following') => {
        setDialogType(type);
        setDialogOpen(true);
    };

    const handleMainFollowToggle = async () => {
        if (!isLoggedIn) return alert("You must be logged in to follow users.");
        if (!profileData) return;

        const isCurrentlyFollowing = profileData.isFollowedByCurrentUser;

        setProfileData((prev: any) => ({
            ...prev,
            isFollowedByCurrentUser: !isCurrentlyFollowing,
            followersCount: isCurrentlyFollowing ? prev.followersCount - 1 : prev.followersCount + 1
        }));

        const result = isCurrentlyFollowing
            ? await followerService.unfollow(profileData.id)
            : await followerService.follow(profileData.id);

        if (!result.success) {
            setProfileData((prev: any) => ({
                ...prev,
                isFollowedByCurrentUser: isCurrentlyFollowing,
                followersCount: isCurrentlyFollowing ? prev.followersCount + 1 : prev.followersCount - 1
            }));

            setErrorMessage(result.message);
        }
    };

    const handleSaveBio = async () => {
        if (!profileData) return;

        const result = await userService.updateMyProfile(draftBio);
        if (result.success) {
            setProfileData({ ...profileData, bio: draftBio });
            setIsEditingBio(false);
        } else {
            setErrorMessage(result.message);
        }
    };

    const handleSaveAvatar = async (newUrl: string) => {
        if (!profileData) return;

        const result = await userService.updateMyProfile(undefined, newUrl);
        if (result.success) {
            setProfileData({ ...profileData, profilePictureUrl: newUrl });
            if (user) {
                setUser({ ...user, profilePictureUrl: newUrl });
            }
            setEditAvatarDialogOpen(false);
        } else {
            setErrorMessage(result.message);
        }
    };

    if (loading)
        return <Box p={5} display="flex" justifyContent="center"><CircularProgress /></Box>;

    if (!profileData)
        return <Typography>{errorMessage}</Typography>;

    if (errorMessage)
        return <Typography>{errorMessage}</Typography>;

    return <Box>
        {/* TOP HEADER SECTION */}
        <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, alignItems: 'center', gap: 4, mb: 2 }}>
            {/* Profile Picture and User Information */}
            <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, alignItems: 'center', textAlign: 'center', minWidth: 250, flexShrink: 0, gap: 4 }}>
                {/* Profile Picture */}
                <Badge
                    overlap="circular"
                    anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
                    badgeContent={
                        isOwner ? (
                            <IconButton
                                size="small"
                                sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', '&:hover': { bgcolor: 'grey.200' } }}
                                onClick={() => setEditAvatarDialogOpen(true)}
                            >
                                <EditIcon fontSize="small" />
                            </IconButton>
                        ) : null
                    }
                >
                    <Avatar sx={{ width: 150, height: 150, fontSize: '3rem' }} src={profileData.profilePictureUrl}>
                        {username?.charAt(0).toUpperCase()}
                    </Avatar>
                </Badge>

                <Box ml={4} mr={4} gap={1} display='flex' flexDirection='column' alignItems='center' justifyContent='space-around'>
                    {/* User Information */}
                    <Box display="flex" flexDirection="row" alignItems="center" gap={1}>
                        <Typography variant="h5" fontWeight="bold">
                            {username}
                        </Typography>
                        <Typography variant='body2' color="text.secondary">
                            •
                        </Typography>
                        <Typography variant="body1" color="text.secondary">
                            Joined {profileData.createdAt}
                        </Typography>
                    </Box>

                    {/* Follower Counts */}
                    <Box sx={{ display: 'flex', gap: 3, justifyContent: 'center' }}>
                        <Box onClick={() => handleOpenDialog('followers')} sx={{ cursor: 'pointer', '&:hover': { opacity: 0.7 } }}>
                            <Typography variant="h6" fontWeight="bold">
                                {profileData.followersCount}
                            </Typography>
                            <Typography variant="caption">
                                Followers
                            </Typography>
                        </Box>

                        <Box onClick={() => handleOpenDialog('following')} sx={{ cursor: 'pointer', '&:hover': { opacity: 0.7 } }}>
                            <Typography variant="h6" fontWeight="bold">
                                {profileData.followingCount}
                            </Typography>
                            <Typography variant="caption">
                                Following
                            </Typography>
                        </Box>

                        {!isOwner && (
                            <Button
                                variant={profileData.isFollowedByCurrentUser ? "outlined" : "contained"}
                                sx={{ borderRadius: 5, textTransform: 'none', fontWeight: 'bold' }}
                                onClick={handleMainFollowToggle}
                            >
                                {profileData.isFollowedByCurrentUser ? 'Unfollow' : 'Follow'}
                            </Button>
                        )}
                    </Box>
                </Box>
            </Box>

            {/* Bio Section */}
            <Paper variant="outlined" sx={{ flexGrow: 1, flexBasis: 0, minWidth: 0, minHeight: 120, p: 3, borderRadius: 2, position: 'relative' }}>
                {isEditingBio ? (
                    <Box>
                        <TextField
                            fullWidth multiline rows={3} variant="outlined"
                            value={draftBio}
                            onChange={(e: any) => setDraftBio(e.target.value)}
                            placeholder="Write something about yourself..."
                            error={isBioOver}
                            helperText={<Box component="span" sx={{ float: 'right' }}>{draftBio.length} / 150</Box>}
                        />
                        <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1, mt: 1 }}>
                            <Button size="small" color="inherit" onClick={() => setIsEditingBio(false)}>
                                Cancel
                            </Button>
                            <Button size="small" variant="contained" onClick={handleSaveBio} disabled={isBioOver}>
                                Save
                            </Button>
                        </Box>
                    </Box>
                ) : (
                    <Box>
                        {isOwner && (
                            <IconButton
                                size="small"
                                onClick={() => {
                                    setDraftBio(profileData.bio || '');
                                    setIsEditingBio(true);
                                }}
                                sx={{ position: 'absolute', top: 8, right: 8 }}
                            >
                                <EditIcon fontSize="small" />
                            </IconButton>
                        )}
                        <Typography variant="body1" color={profileData.bio ? "text.primary" : "text.secondary"} sx={{ whiteSpace: 'pre-wrap' }}>
                            {profileData.bio || `Welcome to ${username}'s profile!`}
                        </Typography>
                    </Box>
                )}
            </Paper>
        </Box >

        {/* TAB NAVIGATION */}
        < Tabs
            value={currentTab}
            onChange={handleTabChange}
            variant='standard' centered
            sx={{ borderBottom: 1, borderTop: 1, borderColor: 'divider', mb: 3 }}
        >
            <Tab label="Overview" value="overview" />
            <Tab label="Reviews" value="reviews" />
            <Tab label="Collections" value="collections" />
        </Tabs>

        {/* NESTED ROUTE RENDERER */}
        < Outlet />

        {/* FOLLOWER/FOLLOWING LIST DIALOG */}
        < FollowerListDialog
            open={dialogOpen}
            onClose={() => setDialogOpen(false)}
            username={username!}
            type={dialogType}

            onFollowerRemoved={() => {
                setProfileData((prev: any) => ({
                    ...prev,
                    followersCount: prev.followersCount - 1
                }));
            }}

            onFollowingCountChanged={(delta) => {
                setProfileData((prev: any) => ({
                    ...prev,
                    followingCount: prev.followingCount + delta
                }));
            }}
        />

        {/* EDIT AVATAR DIALOG */}
        <EditAvatarDialog
            open={editAvatarDialogOpen}
            onClose={() => setEditAvatarDialogOpen(false)}
            currentAvatarUrl={profileData.profilePictureUrl || ''}
            onSave={handleSaveAvatar}
        />
    </Box >
}