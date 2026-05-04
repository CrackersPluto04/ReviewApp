import { useState, useEffect } from "preact/hooks";
import { useSearchParams } from "react-router-dom";
import { mediaService } from "../services/MediaService";
import { TmdbParams } from "../types/types";
import { Box, Typography, FormControl, InputLabel, Select, MenuItem, Button, CircularProgress, TextField } from "@mui/material";
import { MediaCard } from "../components/MediaCard";
import { MyPagination } from "../components/MyPagination";

export function DiscoverPage() {
    const [searchParams, setSearchParams] = useSearchParams();

    // --- State ---
    const [results, setResults] = useState<any[]>([]);
    const [totalPages, setTotalPages] = useState(1);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    // --- Active States (From URL) ---
    const typeUrl = searchParams.get('type') || 'movie'; // 'movie' or 'series'
    const pageUrl = Number.parseInt(searchParams.get('page') || '1', 10);
    const sortByUrl = searchParams.get('sortBy') || 'popularity.desc';
    const yearUrl = searchParams.get('year') || '';
    const withGenresUrl = searchParams.get('withGenres') || '';
    const minRuntimeUrl = searchParams.get('minRuntime') || '';
    const maxRuntimeUrl = searchParams.get('maxRuntime') || '';

    // --- Local States (For Inputs) ---
    const [draftFilters, setDraftFilters] = useState({
        type: typeUrl,
        sortBy: sortByUrl,
        year: yearUrl,
        withGenres: withGenresUrl,
        minRuntime: minRuntimeUrl,
        maxRuntime: maxRuntimeUrl
    });

    // --- Sync URL params to local state ---
    useEffect(() => {
        setDraftFilters({
            type: typeUrl,
            sortBy: sortByUrl,
            year: yearUrl,
            withGenres: withGenresUrl,
            minRuntime: minRuntimeUrl,
            maxRuntime: maxRuntimeUrl
        });
    }, [typeUrl, sortByUrl, yearUrl, withGenresUrl, minRuntimeUrl, maxRuntimeUrl]);

    // --- Fetch Logic ---
    useEffect(() => {
        const fetchDiscover = async () => {
            setLoading(true);
            setErrorMessage('');

            const params: TmdbParams = { page: pageUrl, sortBy: sortByUrl, year: yearUrl, withGenres: withGenresUrl, minRuntime: minRuntimeUrl, maxRuntime: maxRuntimeUrl };
            const res = typeUrl === 'movie'
                ? await mediaService.discoverMovies(params)
                : await mediaService.discoverSeries(params);

            if (res.success) {
                setResults(res.data.items);
                setTotalPages(res.data.totalPages);
            } else
                setErrorMessage(res.message);

            setLoading(false);
        };

        fetchDiscover();
    }, [typeUrl, pageUrl, sortByUrl, yearUrl, withGenresUrl, minRuntimeUrl, maxRuntimeUrl]);

    // --- Handlers ---
    const updateFilter = (key: string, value: string) => {
        setDraftFilters(prev => ({ ...prev, [key]: value }));
    };

    const handlePageChange = (_event: any, newPage: number) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            newParams.set('page', newPage.toString());
            return newParams;
        });
    };

    const handleApply = () => {
        const newParams = new URLSearchParams();

        if (draftFilters.type) newParams.set('type', draftFilters.type);
        if (draftFilters.sortBy) newParams.set('sortBy', draftFilters.sortBy);
        if (draftFilters.year) newParams.set('year', draftFilters.year);
        if (draftFilters.withGenres) newParams.set('withGenres', draftFilters.withGenres);
        if (draftFilters.minRuntime) newParams.set('minRuntime', draftFilters.minRuntime);
        if (draftFilters.maxRuntime) newParams.set('maxRuntime', draftFilters.maxRuntime);

        newParams.set('page', '1');

        setSearchParams(newParams);
    };

    const handleClear = () => {
        setDraftFilters({
            type: draftFilters.type,
            sortBy: 'popularity.desc',
            year: '',
            withGenres: '',
            minRuntime: '',
            maxRuntime: ''
        });

        setSearchParams({ type: draftFilters.type });
    };

    return <Box sx={{ p: 3 }}>
        <Typography variant="h4" mb={3}>
            Discover {typeUrl === 'movie' ? 'Movies' : 'Series'}
        </Typography>

        {/* --- FILTER BAR --- */}
        <Box sx={{ display: 'flex', gap: 2, mb: 4, flexWrap: 'wrap' }}>
            {/* Type Toggler */}
            <FormControl sx={{ minWidth: 120 }}>
                <InputLabel>Type</InputLabel>

                <Select value={draftFilters.type} label="Type" onChange={(e) => updateFilter('type', (e.target as HTMLInputElement).value)}>
                    <MenuItem value="movie">Movies</MenuItem>
                    <MenuItem value="series">Series</MenuItem>
                </Select>
            </FormControl>

            {/* Sort By */}
            <FormControl sx={{ minWidth: 180 }}>
                <InputLabel>Sort By</InputLabel>

                <Select value={draftFilters.sortBy} label="Sort By" onChange={(e) => updateFilter('sortBy', (e.target as HTMLInputElement).value)}>
                    <MenuItem value="popularity.desc">Most Popular</MenuItem>
                    <MenuItem value="primary_release_date.desc">Newest Releases</MenuItem>
                    <MenuItem value="primary_release_date.asc">Oldest Releases</MenuItem>
                    <MenuItem value="original_title.asc">A-Z</MenuItem>
                    <MenuItem value="runtime.desc">Longest Runtime</MenuItem>
                </Select>
            </FormControl>

            {/* Genre */}
            <FormControl sx={{ minWidth: 150 }}>
                <InputLabel>Genre</InputLabel>

                <Select value={draftFilters.withGenres} label="Genre" onChange={(e) => updateFilter('withGenres', (e.target as HTMLInputElement).value)}>
                    <MenuItem value=""><em>Any</em></MenuItem>

                    {draftFilters.type === 'movie' && TMDB_MOVIE_GENRES.map(g => (
                        <MenuItem key={g.id} value={g.id}>{g.name}</MenuItem>
                    ))}

                    {draftFilters.type === 'series' && TMDB_SERIES_GENRES.map(g => (
                        <MenuItem key={g.id} value={g.id}>{g.name}</MenuItem>
                    ))}
                </Select>
            </FormControl>

            {/* Year */}
            <TextField
                label="Year"
                type="number"
                value={draftFilters.year}
                onChange={(e) => updateFilter('year', e.currentTarget.value)}
                slotProps={{ htmlInput: { min: 0 } }}
                sx={{ width: 100 }}
            />

            {/* Min/Max Runtime */}
            <TextField
                label="Min Runtime"
                type="number"
                value={draftFilters.minRuntime}
                onChange={(e) => updateFilter('minRuntime', e.currentTarget.value)}
                slotProps={{ htmlInput: { min: 0 } }}
                sx={{ width: 100 }}
            />
            <TextField
                label="Max Runtime"
                type="number"
                value={draftFilters.maxRuntime}
                onChange={(e) => updateFilter('maxRuntime', e.currentTarget.value)}
                slotProps={{ htmlInput: { min: 0 } }}
                sx={{ width: 100 }}
            />

            <Button variant="contained" color="success" onClick={handleApply}>
                Apply
            </Button>

            <Button variant="contained" color="error" onClick={handleClear}>
                Clear
            </Button>
        </Box>

        {/* --- PAGINATION TOP --- */}
        {totalPages > 1 && !loading &&
            <MyPagination page={pageUrl} totalPages={totalPages} onPageChange={handlePageChange} size="medium" mt={2} mb={2} />}

        {/* --- RESULTS --- */}
        {loading ? (
            <Box display="flex" justifyContent="center" mt={4}><CircularProgress /></Box>
        ) : results.length > 0 ? (
            results.map((item) => (
                <MediaCard key={item.externalApiID} media={item} />
            ))
        ) : (
            <Typography textAlign="center" mt={4} color={errorMessage ? "error" : "text.secondary"}>
                {errorMessage || "No results found for these filters."}
            </Typography>
        )}

        {/* --- PAGINATION BOTTOM --- */}
        {totalPages > 1 && !loading &&
            <MyPagination page={pageUrl} totalPages={totalPages} onPageChange={handlePageChange} size="large" />}
    </Box>
}

const TMDB_MOVIE_GENRES = [
    { id: '28', name: 'Action' },
    { id: '12', name: 'Adventure' },
    { id: '16', name: 'Animation' },
    { id: '35', name: 'Comedy' },
    { id: '80', name: 'Crime' },
    { id: '99', name: 'Documentary' },
    { id: '18', name: 'Drama' },
    { id: '10751', name: 'Family' },
    { id: '14', name: 'Fantasy' },
    { id: '36', name: 'History' },
    { id: '27', name: 'Horror' },
    { id: '10402', name: 'Music' },
    { id: '9648', name: 'Mystery' },
    { id: '10749', name: 'Romance' },
    { id: '878', name: 'Sci-Fi' },
    { id: '10770', name: 'TV Movie' },
    { id: '53', name: 'Thriller' },
    { id: '10752', name: 'War' },
    { id: '37', name: 'Western' }
];

const TMDB_SERIES_GENRES = [
    { id: '10759', name: 'Action & Adventure' },
    { id: '16', name: 'Animation' },
    { id: '35', name: 'Comedy' },
    { id: '80', name: 'Crime' },
    { id: '99', name: 'Documentary' },
    { id: '18', name: 'Drama' },
    { id: '10751', name: 'Family' },
    { id: '10762', name: 'Kids' },
    { id: '9648', name: 'Mystery' },
    { id: '10763', name: 'News' },
    { id: '10764', name: 'Reality' },
    { id: '10765', name: 'Sci-Fi & Fantasy' },
    { id: '10766', name: 'Soap' },
    { id: '10767', name: 'Talk' },
    { id: '10768', name: 'War & Politics' },
    { id: '37', name: 'Western' }
];