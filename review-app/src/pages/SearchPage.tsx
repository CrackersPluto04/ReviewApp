import { useState, useEffect } from 'preact/hooks';
import { useSearchParams } from 'react-router-dom';
import { Box, Typography, Tabs, Tab, CircularProgress } from '@mui/material';
import { SearchBar } from '../components/SearchBar';
import { MediaCard } from '../components/MediaCard';
import { mediaService } from '../services/MediaService';

export function SearchPage() {
    const [searchParams, setSearchParams] = useSearchParams();
    const [results, setResults] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);

    // Read from the URL parameters
    const query = searchParams.get('q') || '';
    const currentType = searchParams.get('type') || 'all';

    useEffect(() => {
        if (!query) return;

        const fetchData = async () => {
            setLoading(true);
            let res;

            switch (currentType) {
                case 'movie':
                    res = await mediaService.searchMovies(query);
                    break;
                case 'series':
                    res = await mediaService.searchSeries(query);
                    break;
                case 'music':
                    res = await mediaService.searchMusic(query);
                    break;
                default:
                    res = await mediaService.searchAll(query);
                    break;
            }

            if (res.success)
                setResults(res.data);

            setLoading(false);
        };

        fetchData();
    }, [query, currentType]);

    const handleTabChange = (_event: Event, newValue: string) => {
        setSearchParams({ q: query, type: newValue });
    };

    return <Box>
        {/* Top Search Area */}
        <Box sx={{ mb: 4, display: 'flex', alignItems: 'center', gap: 2 }}>
            <Typography variant="h6" sx={{ minWidth: 150 }}>Search results for:</Typography>

            <Box sx={{ flexGrow: 1 }}>
                <SearchBar defaultType={currentType as any} />
            </Box>
        </Box>

        {/* The Filters (Tabs) */}
        <Tabs value={currentType} onChange={handleTabChange} variant="fullWidth" sx={{ mb: 4, borderBottom: 1, borderColor: 'divider' }}>
            <Tab label="Any" value="all" />
            <Tab label="Movies" value="movie" />
            <Tab label="Series" value="series" />
            <Tab label="Music" value="music" />
        </Tabs>

        {/* The Results List */}
        {loading ? (
            <Box display="flex" justifyContent="center" mt={4}><CircularProgress /></Box>
        ) : results.length > 0 ? (
            results.map((item) => (
                <MediaCard key={item.externalApiID} media={item} />
            ))
        ) : (
            <Typography textAlign="center" mt={4} color="text.secondary">
                {query ? "No results found. Try a different search." : "Type something above to start searching!"}
            </Typography>
        )}
    </Box>
}