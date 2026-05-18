import { useEffect, useState } from 'preact/hooks';
import { Box, TextField, Button, FormControl, MenuItem, Select } from '@mui/material';
import { useNavigate } from 'react-router-dom';

type SearchBarProps = {
    defaultType?: 'all' | 'movie' | 'series' | 'music';
    placeholder?: string;
    defaultQuery?: string;
    showTypeSelect?: boolean;
}

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
        {/* Conditional Dropdown Menu to search exact media type*/}
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