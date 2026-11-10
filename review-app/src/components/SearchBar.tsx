import { useEffect, useState } from 'preact/hooks';
import { Box, TextField, Button, FormControl, MenuItem, Select } from '@mui/material';
import { useNavigate } from 'react-router-dom';

type SearchBarProps = {
    /** The media type selected at the start; follows later changes of the prop (e.g. the search page's tabs). */
    defaultType?: 'all' | 'movie' | 'series' | 'music';
    placeholder?: string;
    /** The text the field starts with; read once. */
    defaultQuery?: string;
    /** Shows the type dropdown. The search page leaves it out and picks the type with its tabs. */
    showTypeSelect?: boolean;
}

/** The media search form. Submitting goes to `/search` (page 1) with the query and type; blank input does nothing. */
export function SearchBar({ defaultType = 'all', placeholder = "Search...", defaultQuery = "", showTypeSelect = false }: SearchBarProps) {
    const navigate = useNavigate();
    const [query, setQuery] = useState(defaultQuery);
    const [selectedType, setSelectedType] = useState(defaultType);

    useEffect(() => {
        setSelectedType(defaultType);
    }, [defaultType]);

    const handleSearch = (e: Event) => {
        e.preventDefault();
        if (!query.trim()) return;

        navigate(`/search?q=${encodeURIComponent(query)}&type=${selectedType}&page=1`);
    };

    return <Box component="form" onSubmit={handleSearch} sx={{ display: 'flex', gap: 2, w: '100%' }}>
        {showTypeSelect && (
            <FormControl sx={{ minWidth: 120 }}>
                <Select
                    value={selectedType}
                    onChange={(e: any) => setSelectedType(e.target.value)}
                    sx={{ height: '100%', borderRadius: 1 }}
                >
                    <MenuItem value="all">All Media</MenuItem>
                    <MenuItem value="movie">Movies</MenuItem>
                    <MenuItem value="series">Series</MenuItem>
                    <MenuItem value="music">Music</MenuItem>
                </Select>
            </FormControl>
        )}

        <TextField
            fullWidth
            variant="outlined"
            placeholder={placeholder}
            value={query}
            onChange={(e) => setQuery(e.currentTarget.value)}
        />

        <Button type="submit" variant="contained" size="large" sx={{ borderRadius: 2 }}>
            Search
        </Button>
    </Box>
}