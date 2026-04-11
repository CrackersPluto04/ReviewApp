import { Box, Typography, Container, Stack } from '@mui/material';
import { SearchBar } from '../components/SearchBar';

export function HomePage() {
    return <Box sx={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <Stack direction="column" justifyContent="center" alignItems="center" spacing={1}>
            <Typography variant="h3" fontWeight="bold">
                Welcome to THE Review App! 🎬🍿
            </Typography>

            <Typography variant="h5" color="textPrimary">
                Tons of reviews, different opinions. Time to make your own.
            </Typography>
        </Stack>

        <SearchBar />
    </Box>
}