import { AppBar, Toolbar, Typography, Button, IconButton, Box, Container } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import NotificationsIcon from '@mui/icons-material/Notifications';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';
import { useNavigate } from 'react-router-dom';
import { useState } from 'preact/hooks';

export type HeaderProps = {
    mode: 'light' | 'dark';
    toggleTheme: () => void;
};

export function Header({ mode, toggleTheme }: HeaderProps) {
    const navigate = useNavigate();
    // TODO const [loggedIn, setLoggedIn] = useState(!!localStorage.getItem('jwt_token'));

    return <AppBar position="static" color="default" elevation={1}>
        <Container disableGutters maxWidth="xl" sx={{ pr: 12, pl: 12 }}>
            <Toolbar disableGutters sx={{ justifyContent: 'space-between' }}>
                {/* LEFT: Logo */}
                <Typography variant="h6" fontWeight="bold" sx={{ cursor: 'pointer' }}
                    onClick={() => navigate('/')}
                >
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
                    <IconButton color="inherit" onClick={() => navigate('/profile')}>
                        <AccountCircleIcon />
                    </IconButton>
                </Box>
            </Toolbar>
        </Container>
    </AppBar>
}