import { AppBar, Toolbar, Typography, Button, IconButton, Box, Container, Avatar, Divider, Menu, MenuItem } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';
import { useLocation, useNavigate } from 'react-router-dom';
import { useState } from 'preact/hooks';
import { useAuth } from '../context/AuthContext';

export type HeaderProps = {
    mode: 'light' | 'dark';
    toggleTheme: () => void;
};

export function Header({ mode, toggleTheme }: HeaderProps) {
    const navigate = useNavigate();
    const location = useLocation();
    const { user, isLoggedIn, logout } = useAuth();
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
                {/* LEFT: Logo */}
                <Typography variant="h6" fontWeight="bold" sx={{ cursor: 'pointer' }} onClick={() => navigate('/')}>
                    THE Review App
                </Typography>

                {/* MIDDLE: Navigation Links */}
                <Box sx={{ display: 'flex', gap: 2 }}>
                    <Button color="inherit" onClick={() => navigate('/discover?type=movie')}>Movies</Button>
                    <Button color="inherit" onClick={() => navigate('/discover?type=series')}>Series</Button>
                    <Button color="inherit" onClick={() => navigate('/discover/music')}>Music</Button>
                </Box>

                {/* RIGHT: Icons */}
                <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
                    <IconButton color="inherit" onClick={() => navigate('/search')}>
                        <SearchIcon />
                    </IconButton>

                    <IconButton color="inherit" onClick={toggleTheme}>
                        {mode === 'dark' ? <Brightness7Icon /> : <Brightness4Icon />}
                    </IconButton>

                    {isLoggedIn ? (
                        <>
                            <IconButton onClick={handleProfileClick} sx={{ p: 0, ml: 5 }}>
                                <Avatar
                                    alt="Profile Picture"
                                    src={user?.profilePictureUrl}
                                />
                            </IconButton>

                            {/* Dropdown Menu */}
                            <Menu
                                anchorEl={anchorEl}
                                open={isMenuOpen}
                                onClose={handleMenuClose}
                                transformOrigin={{ horizontal: 'right', vertical: 'top' }}
                                anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
                            >
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/overview`)}>My Profile</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/edit`)}>Edit Profile</MenuItem>

                                <Divider />

                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/followers`)}>Followers</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/reviews`)}>Reviews</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/collections`)}>Collections</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/statistics`)}>Statistics</MenuItem>
                                <MenuItem onClick={() => handleNavigate(`/profile/${user?.username}/badges`)}>Badges</MenuItem>

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