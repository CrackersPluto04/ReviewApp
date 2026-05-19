import { Box, Avatar, Typography, Paper, Tabs, Tab } from "@mui/material";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";

export function ProfileLayout() {
    const { username } = useParams();
    const navigate = useNavigate();
    const location = useLocation();

    // Get active tab based on the URL
    const currentTab = location.pathname.split('/').pop() || 'collections';

    const handleTabChange = (_event: any, newValue: string) => {
        navigate(`/profile/${username}/${newValue}`);
    };

    return <Box sx={{ mt: 2 }}>
        {/* TOP HEADER SECTION */}
        <Box sx={{ display: 'flex', gap: 4, mb: 4, alignItems: 'flex-start' }}>
            {/* Profile Avatar & Stats */}
            <Box sx={{ textAlign: 'center' }}>
                <Avatar sx={{ width: 120, height: 120, mb: 2, fontSize: '3rem' }}>
                    {username?.charAt(0).toUpperCase()}
                </Avatar>
                <Typography variant="h5" fontWeight="bold">
                    {username}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                    Joined Jan 2024
                </Typography>

                <Box sx={{ display: 'flex', gap: 2, justifyContent: 'center', mt: 2 }}>
                    <Box>
                        <Typography variant="h6">
                            14
                        </Typography>
                        <Typography variant="caption">
                            Followers
                        </Typography>
                    </Box>
                    <Box>
                        <Typography variant="h6">
                            32
                        </Typography>
                        <Typography variant="caption">
                            Following
                        </Typography>
                    </Box>
                </Box>
            </Box>

            {/* Bio Section */}
            <Paper variant="outlined" sx={{ flexGrow: 1, p: 3, borderRadius: 2, minHeight: 120 }}>
                <Typography variant="body1" color="text.secondary">
                    Welcome to {username}'s profile! (Bio feature coming soon...)
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
    </Box>
}