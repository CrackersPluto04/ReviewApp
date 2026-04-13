import { Box, Typography, Button } from '@mui/material';
import { useNavigate } from 'react-router-dom';

type PleaseLoginProps = {
    message?: string;
};

export function PleaseLogin({ message = "You must be logged in to view this." }: PleaseLoginProps) {
    const navigate = useNavigate();

    return <Box sx={{ textAlign: 'center', py: 10 }}>
        <Typography variant="h5" gutterBottom>
            {message}
        </Typography>
        <Button variant="contained" size="large" onClick={() => navigate('/login')} sx={{ mt: 2 }}>
            Log In or Register
        </Button>
    </Box>
}