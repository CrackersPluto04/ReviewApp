import { useState } from 'preact/hooks';
import { Box, Button, Typography, Container, TextField, Grid, Card, CardMedia, CardContent, CircularProgress } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { mediaService } from '../services/MediaService';

export function HomePage() {
    const navigate = useNavigate();

    const [searchQuery, setSearchQuery] = useState('');
    const [movies, setMovies] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    const handleLogout = () => {
        localStorage.removeItem('jwt_token');
        navigate('/login');
    };

    const handleSearch = async (e: Event) => {
        e.preventDefault();
        if (!searchQuery.trim()) return;

        setLoading(true);
        setError('');

        const result = await mediaService.searchMovies(searchQuery);

        if (result.success) {
            setMovies(result.data);
        } else {
            setError(result.message);
        }

        setLoading(false);
    };

    return <Container component="main" maxWidth="lg">
        <Box sx={{ mt: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography component="h1" variant="h4" fontWeight="bold">
                Discover Movies
            </Typography>

            {/* Conditionally render Login or Logout based on token */}
            {localStorage.getItem('jwt_token') ? (
                <Button variant="outlined" color="error" onClick={handleLogout}>Log Out</Button>
            ) : (
                <Button variant="contained" onClick={() => navigate('/login')}>Log In</Button>
            )}
        </Box>

        {/* The Search Bar */}
        <Box component="form" onSubmit={handleSearch} sx={{ mt: 4, display: 'flex', gap: 2 }}>
            <TextField
                fullWidth
                label="Search for a movie..."
                variant="outlined"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.currentTarget.value)}
            />
            <Button type="submit" variant="contained" size="large" disabled={loading}>
                {loading ? <CircularProgress size={24} /> : 'Search'}
            </Button>
        </Box>

        {error && <Typography color="error" sx={{ mt: 2 }}>{error}</Typography>}

        {/* The Movie Grid */}
        <Grid container spacing={4} sx={{ mt: 2, mb: 8 }}>
            {movies.map((movie) => (
                <Grid key={movie.externalApiId} size={{ xs: 12, sm: 6, md: 4, lg: 3 }}>
                    <Card sx={{ height: '100%', display: 'flex', flexDirection: 'column', transition: '0.3s', '&:hover': { transform: 'scale(1.03)' } }}>
                        <CardMedia
                            component="img"
                            height="400"
                            image={movie.posterUrl || 'https://via.placeholder.com/500x750?text=No+Poster'}
                            alt={movie.title}
                            sx={{ objectFit: 'cover' }}
                        />
                        <CardContent sx={{ flexGrow: 1 }}>
                            <Typography gutterBottom variant="h6" component="h2" noWrap>
                                {movie.title}
                            </Typography>
                            <Typography variant="body2" color="text.secondary">
                                {movie.releaseDate ? movie.releaseDate : 'Unknown Year'}
                            </Typography>
                        </CardContent>
                    </Card>
                </Grid>
            ))}
        </Grid>
    </Container>
}