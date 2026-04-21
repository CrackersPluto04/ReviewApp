import { Card, CardMedia, CardContent, Typography, Box, IconButton, Divider, CircularProgress } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import FavoriteBorderIcon from '@mui/icons-material/FavoriteBorder';
import { MediaDto } from '../types/types';
import { useNavigate } from 'react-router-dom';
import { useEffect, useState } from 'preact/compat';
import { reviewService } from '../services/ReviewService';

type MediaCardProps = {
    media: MediaDto;
}

export function MediaCard({ media }: MediaCardProps) {
    const navigate = useNavigate();

    const [stats, setStats] = useState({ averageScore: 0, reviewCount: 0 });
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    useEffect(() => {
        const fetchAverageScore = async () => {
            setLoading(true);

            const result = await reviewService.getAverageScore(media.externalApiID, media.mediaType);
            if (result.success)
                setStats(result.data);
            else
                setErrorMessage(result.message);

            setLoading(false);
        }

        fetchAverageScore();
    }, [media]);

    const handleCardClick = () => {
        navigate('/media', { state: { media } })
    }

    return <Card
        sx={{
            display: 'flex', height: 200, mb: 2,
            borderRadius: 5, border: '3px solid', borderColor: 'grey.300', boxShadow: 5,
            transition: 'transform 0.2s ease-in-out, box-shadow 0.2s ease-in-out',
            '&:hover': {
                transform: 'scale(1.02)',
                boxShadow: 8,
            }
        }}>
        {/* LEFT: Poster */}
        <CardMedia
            component="img"
            onClick={handleCardClick}
            sx={{ width: 150, minWidth: 150, flexShrink: 0, objectFit: 'cover', cursor: 'pointer' }}
            image={media.posterUrl || 'https://placehold.co/140x200?text=No+Poster'}
            alt={media.title}
        />

        {/* MIDDLE: Details */}
        <CardContent sx={{ m: 1, flexGrow: 1, display: 'flex', flexDirection: 'column', justifyContent: 'space-between', overflow: 'hidden' }}>
            <Box sx={{ my: 0.5 }}>
                <Typography variant="h5" fontWeight="bold" noWrap onClick={handleCardClick} sx={{ cursor: 'pointer' }}>
                    {media.title}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                    {media.releaseDate ? media.releaseDate : 'Unknown Year'}
                </Typography>
            </Box>

            <Typography variant="body2" sx={{ mb: 1, overflow: 'hidden', textOverflow: 'ellipsis', display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical' }}>
                {media.mediaType === 2 ? media.creator || 'Unknown Creator' : media.overview || 'No overview available for this title.'}
            </Typography>
        </CardContent>

        {/* Divider */}
        <Divider orientation="vertical" flexItem sx={{ borderStyle: 'dashed', borderWidth: 1 }} />

        {/* RIGHT: Stats & Actions */}
        <Box sx={{ width: 160, minWidth: 160, flexShrink: 0, display: 'flex', flexDirection: 'column', justifyContent: 'space-around', alignItems: 'center', p: 2, bgcolor: 'background.default' }}>
            {loading ? (
                <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
            ) : errorMessage ? (
                <Typography variant="h6" color='error' fontWeight="bold">
                    {errorMessage}
                </Typography>
            ) : (
                <>
                    <Typography variant="h6" color={stats.reviewCount === 0 ? 'textPrimary' : stats.averageScore >= 7 ? 'success' : stats.averageScore >= 4 ? 'warning' : 'error'} fontWeight="bold">
                        {stats.reviewCount > 0 ? `${stats.averageScore} / 10` : '-- / 10'}
                    </Typography>
                    <Typography variant="caption" color='textSecondary' mb={2}>
                        {stats.reviewCount} {stats.reviewCount === 1 ? 'review' : 'reviews'}
                    </Typography>
                </>
            )}

            <Box sx={{ display: 'flex', gap: 1 }}>
                <IconButton size="small" color="primary" title="Add to Collection">
                    <EditIcon />
                </IconButton>
                <IconButton size="small" color="error" title="Add to Favorite">
                    <FavoriteBorderIcon />
                </IconButton>
            </Box>
        </Box>
    </Card >
}