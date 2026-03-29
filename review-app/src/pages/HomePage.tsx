import { Container, Box, Typography, Button } from "@mui/material";
import { useState } from "preact/hooks";
import { useNavigate } from "react-router-dom";

export function HomePage() {
    const navigate = useNavigate();

    const [loggedIn, setLoggedIn] = useState(!!localStorage.getItem('jwt_token'));

    const handleLogout = () => {
        localStorage.removeItem('jwt_token');
        setLoggedIn(false);
        navigate('/login');
    }

    return <Container component="main" maxWidth="md">
        <Box sx={{ mt: 8, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <Typography component="h1" variant="h3" gutterBottom>
                Dummy HomePage!
            </Typography>
            <Typography variant="h6" color="text.secondary" sx={{ mb: 4 }}>
                (The media and reviews will go here later when the C# backend is ready.)
            </Typography>

            <Button variant="outlined" color={loggedIn ? "error" : "success"}
                onClick={loggedIn ? handleLogout : () => navigate('/login')}
            >
                {loggedIn ? 'Log Out' : 'Log In'}
            </Button>

            <Button variant="outlined" color="secondary" onClick={() => navigate('/profile')}>
                View Profile
            </Button>
        </Box>
    </Container>
}