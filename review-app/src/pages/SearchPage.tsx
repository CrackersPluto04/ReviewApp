import { useState, useEffect } from 'preact/hooks';
import { useSearchParams } from 'react-router-dom';
import { Box, Typography, Tabs, Tab, CircularProgress, Button, Grid, Paper } from '@mui/material';
import { SearchBar } from '../components/SearchBar';
import { MediaCard } from '../components/MediaCard';
import { mediaService } from '../services/MediaService';
import { MyPagination } from '../components/MyPagination';

export function SearchPage() {
    const [searchParams, setSearchParams] = useSearchParams();

    const [results, setResults] = useState<any[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [totalPages, setTotalPages] = useState(1);

    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    // Read from the URL parameters
    const query = searchParams.get('q') || '';
    const currentType = searchParams.get('type') || 'all';
    const page = Number.parseInt(searchParams.get('page') || '1', 10);

    useEffect(() => {
        if (!query) return;

        const fetchData = async () => {
            setLoading(true);
            let res;

            switch (currentType) {
                case 'movie':
                    res = await mediaService.searchMovies(query, page);
                    break;
                case 'series':
                    res = await mediaService.searchSeries(query, page);
                    break;
                case 'music':
                    res = await mediaService.searchMusic(query, page);
                    break;
                default:
                    res = await mediaService.searchAll(query);
                    break;
            }

            if (res.success) {
                setResults(res.data.items);
                setTotalCount(res.data.totalCount);
                setTotalPages(res.data.totalPages);
            }
            else
                setErrorMessage(res.message);

            setLoading(false);
        };

        fetchData();
    }, [query, currentType, page]);

    const handlePageChange = (_event: any, newPage: number) => {
        setSearchParams({ q: query, type: currentType, page: newPage.toString() });
    };

    const handleTabChange = (_event: Event, newValue: string) => {
        setSearchParams({ q: query, type: newValue, page: '1' });
    };

    return <Box>
        {/* Top Search Area */}
        <Box sx={{ mb: 4, display: 'flex', alignItems: 'center', gap: 2 }}>
            <Typography variant="h6" sx={{ minWidth: 150 }}>
                Search results for:
            </Typography>

            <Box sx={{ flexGrow: 1 }}>
                <SearchBar defaultType={currentType as any} defaultQuery={query} />
            </Box>
        </Box>

        {/* Tabs */}
        <Tabs value={currentType} onChange={handleTabChange} variant="fullWidth" sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
            <Tab label="Any" value="all" disabled={loading} />
            <Tab label="Movies" value="movie" disabled={loading} />
            <Tab label="Series" value="series" disabled={loading} />
            <Tab label="Music" value="music" disabled={loading} />
        </Tabs>

        {/* Pagination Top */}
        {totalPages > 1 && !loading &&
            <MyPagination page={page} totalPages={totalPages} onPageChange={handlePageChange} size="medium" mt={2} mb={2} />}

        {/* Results List */}
        {loading ? (
            <Box display="flex" justifyContent="center" mt={4}><CircularProgress /></Box>
        ) : results.length > 0 ? (
            results.map((item) => (
                <MediaCard key={item.externalApiID} media={item} />
            ))
        ) : (
            <Typography textAlign="center" mt={4} color={errorMessage ? "error" : "text.secondary"}>
                {query ? errorMessage || "No results found. Try a different search." : "Type something above to start searching!"}
            </Typography>
        )}

        {/* Pagination Bottom */}
        {totalPages > 1 && !loading &&
            <MyPagination page={page} totalPages={totalPages} onPageChange={handlePageChange} size="large" />}

        {/* Search Specific Media Buttons */}
        {currentType === 'all' && results.length > 0 && !loading && (
            <Box display="flex" justifyContent="center" mt={4} gap={2}>
                <Button variant="outlined" onClick={() => setSearchParams({ q: query, type: 'movie', page: '1' })}>
                    See all Movies
                </Button>
                <Button variant="outlined" onClick={() => setSearchParams({ q: query, type: 'series', page: '1' })}>
                    See all Series
                </Button>
                <Button variant="outlined" onClick={() => setSearchParams({ q: query, type: 'music', page: '1' })}>
                    See all Music
                </Button>
            </Box>
        )}
    </Box>
}