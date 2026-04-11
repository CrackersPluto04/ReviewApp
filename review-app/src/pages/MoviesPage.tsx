import { Box, Typography } from "@mui/material";
import { SearchBar } from "../components/SearchBar";

export function MoviesPage() {
    return <Box sx={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <Typography>
            Movies Page
        </Typography>

        <SearchBar defaultType="movie" placeholder="Search for a movie..." />
    </Box>
}