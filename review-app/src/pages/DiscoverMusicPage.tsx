import { useEffect, useState } from "preact/hooks";
import { useSearchParams } from "react-router-dom";
import { mediaService } from "../services/MediaService";
import { MediaDto, SpotifyParams } from "../types/types";
import { Box, Typography, FormControl, InputLabel, Select, MenuItem, TextField, Button, CircularProgress } from "@mui/material";
import { MediaCard } from "../components/MediaCard";
import { MyPagination } from "../components/MyPagination";

export function DiscoverMusicPage() {
    const [searchParams, setSearchParams] = useSearchParams();

    // --- State ---
    const [results, setResults] = useState<MediaDto[]>([]);
    const [totalPages, setTotalPages] = useState(1);
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');

    // --- Active States (From URL) ---
    const pageUrl = Number.parseInt(searchParams.get('page') || '1', 10);
    const genreUrl = searchParams.get('genre') || 'pop';
    const yearUrl = searchParams.get('year') || '';
    const marketUrl = searchParams.get('market') || 'US';

    // Spliting the year parameter
    const yearParts = yearUrl.split('-');
    const activeFromYear = yearParts[0] || '';
    const activeToYear = yearParts.length > 1 ? yearParts[1] : '';

    // --- Local States (For Inputs) ---
    const [draftFilters, setDraftFilters] = useState({
        genre: genreUrl,
        fromYear: activeFromYear,
        toYear: activeToYear,
        market: marketUrl
    });

    // --- Sync local state to URL params ---
    useEffect(() => {
        setDraftFilters({
            genre: genreUrl,
            fromYear: activeFromYear,
            toYear: activeToYear,
            market: marketUrl
        });
    }, [genreUrl, yearUrl, marketUrl]);

    // --- Fetch Logic ---
    useEffect(() => {
        const fetchDiscover = async () => {
            setLoading(true);
            setErrorMessage('');

            const params: SpotifyParams = { page: pageUrl, genre: genreUrl, year: yearUrl, market: marketUrl };
            const res = await mediaService.discoverMusic(params);

            if (res.success) {
                setResults(res.data.items);
                setTotalPages(res.data.totalPages);
            } else {
                setErrorMessage(res.message);
            }

            setLoading(false);
        };

        fetchDiscover();
    }, [pageUrl, genreUrl, yearUrl, marketUrl]);

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

        if (draftFilters.genre) newParams.set('genre', draftFilters.genre);
        if (draftFilters.fromYear && draftFilters.toYear) {
            newParams.set('year', `${draftFilters.fromYear}-${draftFilters.toYear}`);
        } else if (draftFilters.fromYear) {
            newParams.set('year', `${draftFilters.fromYear}`);
        } else if (draftFilters.toYear) {
            newParams.set('year', `${draftFilters.toYear}`);
        }
        if (draftFilters.market) newParams.set('market', draftFilters.market);

        newParams.set('page', '1');

        setSearchParams(newParams);
    };

    const handleClear = () => {
        setDraftFilters({ genre: 'pop', fromYear: '', toYear: '', market: 'US' });
        setSearchParams({});
    };

    return <Box sx={{ p: 3 }}>
        <Typography variant="h4" mb={3}>
            Discover Music
        </Typography>

        {/* --- FILTER BAR --- */}
        <Box sx={{ display: 'flex', gap: 2, mb: 4, flexWrap: 'wrap' }}>
            {/* Genre Dropdown */}
            <FormControl sx={{ minWidth: 150 }}>
                <InputLabel>Genre</InputLabel>

                <Select value={draftFilters.genre} label="Genre" onChange={(e) => updateFilter('genre', (e.target as HTMLInputElement).value)}>
                    {SPOTIFY_GENRES.map(g => (
                        <MenuItem key={g} value={g}>{g.charAt(0).toUpperCase() + g.slice(1)}</MenuItem>
                    ))}
                </Select>
            </FormControl>

            {/* Market Dropdown */}
            <FormControl sx={{ minWidth: 100 }}>
                <InputLabel>Market</InputLabel>

                <Select value={draftFilters.market} label="Market" onChange={(e) => updateFilter('market', (e.target as HTMLInputElement).value)}>
                    {SPOTIFY_MARKETS.map(m => (
                        <MenuItem key={m} value={m}>{m}</MenuItem>
                    ))}
                </Select>
            </FormControl>

            {/* From-To Year */}
            <TextField
                label="From (year)"
                type="number"
                value={draftFilters.fromYear}
                onChange={(e) => updateFilter('fromYear', e.currentTarget.value)}
                slotProps={{ htmlInput: { min: 0 } }}
                sx={{ width: 120 }}
            />
            <TextField
                label="To (year)"
                type="number"
                value={draftFilters.toYear}
                onChange={(e) => updateFilter('toYear', e.currentTarget.value)}
                slotProps={{ htmlInput: { min: 0 } }}
                sx={{ width: 120 }}
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
                <MediaCard key={item.id} media={item} />
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

const SPOTIFY_GENRES = [
    "acoustic", "afrobeat", "alt-rock", "alternative", "ambient", "anime",
    "classical", "country", "dance", "edm", "hip-hop", "indie", "jazz",
    "k-pop", "metal", "pop", "r-n-b", "rock", "synth-pop", "techno"
];

const SPOTIFY_MARKETS = [
    "US", "GB", "CA", "DE", "FR", "IT", "ES", "AU", "BR", "MX",
    "JP", "KR", "SE", "NL", "RU", "IN", "ZA", "AR", "BE", "CH",
    "HU", "RO", "PL", "AT", "GR"
];