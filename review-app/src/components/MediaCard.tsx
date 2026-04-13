import { Card, CardMedia, CardContent, Typography, Box, IconButton, Divider } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import FavoriteBorderIcon from '@mui/icons-material/FavoriteBorder';
import { MediaDto } from '../types/types';
import { useNavigate } from 'react-router-dom';

type MediaCardProps = {
    media: MediaDto;
}

export function MediaCard({ media }: MediaCardProps) {
    const navigate = useNavigate();

    const handleCardClick = () => {
        navigate('/media', { state: { media } })
    }

    return <Card onClick={handleCardClick} sx={{ display: 'flex', height: 200, mb: 2, borderRadius: 5, border: '3px solid', borderColor: 'grey.300', boxShadow: 5, cursor: 'pointer' }}>
        {/* LEFT: Poster */}
        <CardMedia
            component="img"
            sx={{ width: 150, minWidth: 150, flexShrink: 0, objectFit: 'cover' }}
            image={media.posterUrl || 'https://placehold.co/140x200?text=No+Poster'}
            alt={media.title}
        />

        {/* MIDDLE: Details */}
        <CardContent sx={{ m: 1, flexGrow: 1, display: 'flex', flexDirection: 'column', justifyContent: 'space-between', overflow: 'hidden' }}>
            <Box sx={{ my: 0.5 }}>
                <Typography variant="h5" fontWeight="bold" noWrap >
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
            <Typography variant="h6" fontWeight="bold">-- / 10</Typography>
            <Typography variant="caption" color="text.secondary" mb={2}>0 reviews</Typography>

            <Box sx={{ display: 'flex', gap: 1 }}>
                <IconButton size="small" color="primary" title="Write/Edit Review">
                    <EditIcon />
                </IconButton>
                <IconButton size="small" color="error" title="Favorite">
                    <FavoriteBorderIcon />
                </IconButton>
            </Box>
        </Box>
    </Card>
}