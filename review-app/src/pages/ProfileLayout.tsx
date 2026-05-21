import { Box, Avatar, Typography, Paper, Tabs, Tab, CircularProgress, Button } from "@mui/material";
import { useEffect, useState } from "preact/hooks";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { UserProfileDto } from "../types/types";
import { useAuth } from "../context/AuthContext";
import { userService } from "../services/UserService";
import { followerService } from "../services/FollowerService";
import { FollowerListDialog } from "../components/FollowerListDialog";

export function ProfileLayout() {
    const { user, isLoggedIn } = useAuth();
    const { username } = useParams();
    const navigate = useNavigate();
    const location = useLocation();

    // Get active tab based on the URL
    const currentTab = location.pathname.split('/').pop() || 'collections';

    const [profileData, setProfileData] = useState<UserProfileDto>();
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');
    const isOwner = user?.username === username;

    const [dialogOpen, setDialogOpen] = useState(false);
    const [dialogType, setDialogType] = useState<'followers' | 'following'>('followers');

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

    if (loading)
        return <Box p={5} display="flex" justifyContent="center"><CircularProgress /></Box>;

    if (!profileData)
        return <Typography>{errorMessage}</Typography>;

    if (errorMessage)
        return <Typography>{errorMessage}</Typography>;

    return <Box sx={{ mt: 2 }}>
        {/* TOP HEADER SECTION */}
        <Box sx={{ display: 'flex', flexDirection: 'row', textAlign: 'center', justifyContent: 'center', alignItems: 'center', gap: 4, mb: 3 }}>
            <Avatar sx={{ width: 150, height: 150, fontSize: '3rem' }} src={profileData.profilePictureUrl}>
                {username?.charAt(0).toUpperCase()}
            </Avatar>

            <Box>
                <Box display="flex" flexDirection="row" alignItems="center" justifyContent="center" gap={2}>
                    <Typography variant="h5" fontWeight="bold">
                        {username}
                    </Typography>
                    <Typography variant="body1" color="text.secondary">
                        Joined {profileData.createdAt}
                    </Typography>
                </Box>

                <Box sx={{ display: 'flex', gap: 3, justifyContent: 'center', mt: 2, mb: 2 }}>
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

            {/* Bio Section */}
            <Paper variant="outlined" sx={{ flexGrow: 1, textAlign: 'start', p: 3, borderRadius: 2, minHeight: 120 }}>
                <Typography variant="body1" color="text.secondary">
                    {profileData.bio || "This user hasn't written a bio yet."}
                </Typography>
            </Paper>
        </Box>

        {/* TAB NAVIGATION */}
        <Tabs
            value={currentTab}
            onChange={handleTabChange}
            variant='fullWidth'
            sx={{ borderBottom: 1, borderColor: 'divider', mb: 3 }}
        >
            <Tab label="Overview" value="overview" />
            <Tab label="Reviews" value="reviews" />
            <Tab label="Collections" value="collections" />
            <Tab label="Statistics" value="statistics" />
            <Tab label="Badges" value="badges" />
        </Tabs>

        {/* NESTED ROUTE RENDERER */}
        <Outlet />

        {/* FOLLOWER/FOLLOWING LIST DIALOG */}
        <FollowerListDialog
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
    </Box>
}