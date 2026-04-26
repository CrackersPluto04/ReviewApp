import { AppBar, Toolbar, Typography, Button, IconButton, Box, Container, Avatar, Divider, Menu, MenuItem } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import NotificationsIcon from '@mui/icons-material/Notifications';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';
import { useNavigate } from 'react-router-dom';
import { useState } from 'preact/hooks';
import { useAuth } from '../context/AuthContext';

export type HeaderProps = {
    mode: 'light' | 'dark';
    toggleTheme: () => void;
};

export function Header({ mode, toggleTheme }: HeaderProps) {
    const navigate = useNavigate();
    const { isLoggedIn, logout } = useAuth();
    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
    const isMenuOpen = Boolean(anchorEl);

    const handleProfileClick = (e: any) => {
        setAnchorEl(e.currentTarget);
    };

    const handleMenuClose = () => {
        setAnchorEl(null);
    };

    const handleLogout = () => {
        handleMenuClose();
        logout();
    };

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
                    <Button color="inherit" onClick={() => navigate('/movies')}>Movies</Button>
                    <Button color="inherit" onClick={() => navigate('/series')}>Series</Button>
                    <Button color="inherit" onClick={() => navigate('/music')}>Music</Button>
                    <Button color="inherit" sx={{ fontWeight: 'bold' }}>*Live</Button>
                </Box>

                {/* RIGHT: Icons */}
                <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
                    <IconButton color="inherit">
                        <SearchIcon />
                    </IconButton>
                    <IconButton color="inherit">
                        <NotificationsIcon />
                    </IconButton>
                    <IconButton color="inherit" onClick={toggleTheme}>
                        {mode === 'dark' ? <Brightness7Icon /> : <Brightness4Icon />}
                    </IconButton>

                    {isLoggedIn ? (
                        <>
                            <IconButton onClick={handleProfileClick} sx={{ p: 0, ml: 1 }}>
                                {/* TODO: You can pull the actual user's ProfilePictureUrl from localStorage or Context here later! */}
                                <Avatar alt="User Profile" />
                            </IconButton>

                            {/* Dropdown Menu */}
                            <Menu
                                anchorEl={anchorEl}
                                open={isMenuOpen}
                                onClose={handleMenuClose}
                                transformOrigin={{ horizontal: 'right', vertical: 'top' }}
                                anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
                            >
                                <MenuItem onClick={() => handleNavigate('/profile')}>My Profile</MenuItem>
                                <MenuItem onClick={() => handleNavigate('/profile/edit')}>Edit Profile</MenuItem>

                                <Divider />

                                <MenuItem onClick={() => handleNavigate('/followers')}>Followers</MenuItem>
                                <MenuItem onClick={() => handleNavigate('/favourites')}>Favourites</MenuItem>
                                <MenuItem onClick={() => handleNavigate('/collections')}>Collections</MenuItem>

                                <Divider />

                                <MenuItem onClick={handleLogout} sx={{ color: 'error.main' }}>Logout</MenuItem>
                            </Menu>
                        </>
                    ) : (
                        <Button variant='contained' color='success' sx={{ ml: 2, borderRadius: 2 }}
                            onClick={() => navigate('/login')}
                        >
                            Login
                        </Button>
                    )}
                </Box>
            </Toolbar>
        </Container>
    </AppBar>
}