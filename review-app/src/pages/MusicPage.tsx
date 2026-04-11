import { Box, Typography } from "@mui/material";
import { SearchBar } from "../components/SearchBar";

export function MusicPage() {
    return <Box sx={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <Typography>
            Music Page
        </Typography>

        <SearchBar defaultType="music" placeholder="Search for a song..." />
    </Box>
}