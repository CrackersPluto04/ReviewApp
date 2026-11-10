import { Box, Typography, Button } from '@mui/material';
import { useLocation, useNavigate } from 'react-router-dom';

type PleaseLoginProps = {
    /** What the login is needed for. */
    message?: string;
    /** A small inline variant, e.g. under a review in place of the reply form. */
    compact?: boolean;
};

/** A prompt to log in or register. The login page sends the user back to the current location afterwards. */
export function PleaseLogin({ message = "You must be logged in to view this.", compact = false }: PleaseLoginProps) {
    const navigate = useNavigate();
    const location = useLocation();

    const handleLoginClick = () => {
        navigate('/login', { state: { returnTo: location } })
    }

    if (compact) {
        return <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 1 }}>
            <Typography variant="body2" color="text.secondary">
                {message}
            </Typography>
            <Button size="small" onClick={handleLoginClick}>
                Log In or Register
            </Button>
        </Box>
    }

    return <Box sx={{ textAlign: 'center', py: 10 }}>
        <Typography variant="h5" gutterBottom>
            {message}
        </Typography>
        <Button variant="contained" size="large" onClick={handleLoginClick} sx={{ mt: 2 }}>
            Log In or Register
        </Button>
    </Box>
}
