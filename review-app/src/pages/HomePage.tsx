import { Box, Typography, Stack } from '@mui/material';
import { SearchBar } from '../components/SearchBar';

/** The landing page (`/home`): a welcome text and the media search. */
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

        <SearchBar placeholder="Search for a piece of media..." showTypeSelect={true} />
    </Box>
}