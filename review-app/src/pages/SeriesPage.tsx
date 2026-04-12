import { Box, Typography } from "@mui/material";
import { SearchBar } from "../components/SearchBar";

export function SeriesPage() {
    return <Box sx={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <Typography>
            Series Page
        </Typography>

        <SearchBar defaultType="series" placeholder="Search for a series..." />
    </Box>
}