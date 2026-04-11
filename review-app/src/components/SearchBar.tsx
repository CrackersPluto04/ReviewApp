import { useState } from 'preact/hooks';
import { Box, TextField, Button, Container } from '@mui/material';
import { useNavigate } from 'react-router-dom';

type SearchBarProps = {
    defaultType?: 'all' | 'movie' | 'series' | 'music';
    placeholder?: string;
}

export function SearchBar({ defaultType = 'all', placeholder = "Search for any piece of media..." }: SearchBarProps) {
    const navigate = useNavigate();
    const [query, setQuery] = useState('');

    const handleSearch = (e: Event) => {
        e.preventDefault();
        if (!query.trim()) return;

        navigate(`/search?q=${encodeURIComponent(query)}&type=${defaultType}`);
    };

    return <Box component="form" onSubmit={handleSearch} sx={{ display: 'flex', gap: 2, w: '100%' }}>
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