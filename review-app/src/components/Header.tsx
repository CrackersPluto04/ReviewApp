import { AppBar, Toolbar, Typography, Button, IconButton, Box, Container, Avatar, Badge, Divider, Menu, MenuItem, CircularProgress } from '@mui/material';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';
import WorkspacePremiumIcon from '@mui/icons-material/WorkspacePremium';
import { PREMIUM_COLOR } from '../utils/subscription';
import { useLocation, useNavigate } from 'react-router-dom';
import { useState } from 'preact/hooks';
import { useAuth } from '../context/AuthContext';
import { UserSearchBar } from './UserSearchBar';
import { NotificationBell } from './NotificationBell';

export type HeaderProps = {
    mode: 'light' | 'dark';
    toggleTheme: () => void;
};

export function Header({ mode, toggleTheme }: HeaderProps) {
    const navigate = useNavigate();
    const location = useLocation();
    const { user, isLoggedIn, logout, isLoading } = useAuth();
    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
    const isMenuOpen = Boolean(anchorEl);

    const handleProfileClick = (e: any) => {
        setAnchorEl(e.currentTarget);
    };

    const handleMenuClose = () => {
        setAnchorEl(null);
    };

    // Hard reset after logout to clean memory
    const handleLogout = async () => {
        handleMenuClose();
        await logout();
        globalThis.location.href = '/';
    };

    const handleLogin = () => {
        navigate('/login', { state: { returnTo: location } });
    }

    const handleNavigate = (path: string) => {
        handleMenuClose();
        navigate(path);
    };

    return <AppBar position="static" color="default" elevation={1}>
        <Container disableGutters maxWidth="xl" sx={{ pr: 12, pl: 12 }}>
            <Toolbar disableGutters sx={{ justifyContent: 'space-between' }}>
                {/* Logo */}
                <Typography variant="h6" fontWeight="bold" sx={{ cursor: 'pointer' }} onClick={() => navigate('/')}>
                    THE Review App
                </Typography>

                {/* Navigation Links */}
                <Box sx={{ display: 'flex', gap: 2 }}>
                    <Button color="inherit" onClick={() => navigate('/discover?type=movie')}>Movies</Button>
                    <Button color="inherit" onClick={() => navigate('/discover?type=series')}>Series</Button>
                    <Button color="inherit" onClick={() => navigate('/discover/music')}>Music</Button>
                    <Button color="inherit" startIcon={<WorkspacePremiumIcon sx={{ color: PREMIUM_COLOR }} />} onClick={() => navigate('/premium')}>
                        Premium
                    </Button>
                </Box>

                {/* User Search Bar */}
                <UserSearchBar />

                {/* Theme Toggle & Login/Profile */}
                <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
                    <IconButton color="inherit" onClick={toggleTheme}>
                        {mode === 'dark' ? <Brightness7Icon /> : <Brightness4Icon />}
                    </IconButton>

                    {isLoading ? (
                        <Box p={5} display="flex" justifyContent="center"><CircularProgress /></Box>
                    ) : isLoggedIn ? (
                        <>
                            <NotificationBell />

                            <IconButton onClick={handleProfileClick} sx={{ p: 0, ml: 2 }} aria-label={user?.isPremium ? 'Account menu (Premium)' : 'Account menu'}>
                                {/* Small gold ring + badge for subscribers */}
                                <Badge
                                    overlap="circular"
                                    anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
                                    invisible={!user?.isPremium}
                                    badgeContent={<WorkspacePremiumIcon sx={{ fontSize: 16, color: PREMIUM_COLOR, bgcolor: 'background.paper', borderRadius: '50%' }} />}
                                >
                                    <Avatar
                                        alt="Profile Picture"
                                        src={user?.profilePictureUrl}
                                        sx={user?.isPremium ? { outline: `2px solid ${PREMIUM_COLOR}` } : undefined}
                                    />
                                </Badge>
                            </IconButton>

                            {/* Dropdown Menu */}
                            <Menu
                                anchorEl={anchorEl}
                                open={isMenuOpen}
                                onClose={handleMenuClose}
                                transformOrigin={{ horizontal: 'right', vertical: 'top' }}
                                anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
                            >
                                <MenuItem onClick={() => handleNavigate(`/profile/${encodeURIComponent(user?.username ?? '')}/overview`)}>My Profile</MenuItem>

                                <Divider />

                                <MenuItem onClick={() => handleNavigate(`/profile/${encodeURIComponent(user?.username ?? '')}/reviews`)}>Reviews</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${encodeURIComponent(user?.username ?? '')}/collections`)}>Collections</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${encodeURIComponent(user?.username ?? '')}/achievements`)}>Achievements</MenuItem>

                                <Divider />

                                <MenuItem onClick={() => handleNavigate('/premium')}>
                                    {user?.isPremium ? 'Manage subscription' : 'Get Premium'}
                                </MenuItem>

                                <Divider />

                                <MenuItem onClick={handleLogout} sx={{ color: 'error.main' }}>Logout</MenuItem>
                            </Menu>
                        </>
                    ) : (
                        <Button variant='contained' color='success' sx={{ ml: 2, borderRadius: 2 }}
                            onClick={handleLogin}
                        >
                            Login
                        </Button>
                    )}
                </Box>
            </Toolbar>
        </Container>
    </AppBar>
}