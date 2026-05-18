import { Card, CardMedia, CardContent, Typography, Box, IconButton, Divider, CircularProgress, Tooltip, Menu, MenuItem, Alert, Snackbar } from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import PlaylistAddIcon from '@mui/icons-material/PlaylistAdd';
import FavoriteBorderIcon from '@mui/icons-material/FavoriteBorder';
import { CollectionDto, MediaDto } from '../types/types';
import { useNavigate } from 'react-router-dom';
import { useEffect, useState } from 'preact/compat';
import { reviewService } from '../services/ReviewService';
import { useAuth } from '../context/AuthContext';
import { userService } from '../services/UserService';
import { collectionService } from '../services/CollectionService';

type MediaCardProps = {
    media: MediaDto;
    collectionId?: number;
    onRemove?: (mediaType: number, externalApiId: string) => void;
    isOwner?: boolean;
}

export function MediaCard({ media, collectionId, onRemove, isOwner }: MediaCardProps) {
    // Get 'global' variables
    const navigate = useNavigate();
    const { user, isLoggedIn } = useAuth();

    // Set local states
    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
    const [collections, setCollections] = useState<CollectionDto[]>([]);
    const [isMenuLoading, setIsMenuLoading] = useState(false);

    const [toast, setToast] = useState(
        {
            open: false,
            message: '',
            severity: 'success' as 'success' | 'error'
        });

    const [stats, setStats] = useState({ averageScore: 0, reviewCount: 0 });
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    // Fetches the statistics for media
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

    /* Handlers */

    const handleCardClick = () => {
        navigate(`/media/${media.mediaType}/${media.externalApiID}`, { state: { media } })
    }

    const showToast = (message: string, severity: 'success' | 'error') => {
        setToast({ open: true, message, severity });
    };

    // --- ADD TO COLLECTION LOGIC ---
    const handleOpenMenu = async (event: any) => {
        setAnchorEl(event.currentTarget);

        if (collections.length === 0 && user?.username) {
            setIsMenuLoading(true);

            const result = await userService.getUserCollections(user.username);
            if (result.success)
                setCollections(result.data);

            setIsMenuLoading(false);
        }
    };

    const handleCloseMenu = () => setAnchorEl(null);

    const handleAddToCollection = async (targetCollectionId: number) => {
        handleCloseMenu();

        const result = await collectionService.addMediaToCollection(targetCollectionId, media.mediaType, media.externalApiID);
        if (result.success) {
            showToast(`Added to collection!`, 'success');
        } else {
            showToast(result.message, 'error');
        }
    };

    // --- ADD TO FAVOURITES LOGIC ---
    const handleAddToFavorites = async () => {
        if (!user?.username) return;

        // Fetch collections if we haven't already
        let collectionsToSearch = collections;
        if (collectionsToSearch.length === 0) {
            const result = await userService.getUserCollections(user.username);
            if (result.success) {
                collectionsToSearch = result.data;
                setCollections(result.data);
            }
        }

        // Find the default "Favourites" collection
        const favCollection = collectionsToSearch.find(c => c.name.toLowerCase() === 'favourites');

        if (!favCollection) {
            showToast("Could not find your Favourites collection.", 'error');
            return;
        }

        // Add to it
        const result = await collectionService.addMediaToCollection(favCollection.id, media.mediaType, media.externalApiID);
        if (result.success)
            showToast(`Added to Favourites!`, 'success');
        else
            showToast(result.message, 'error');
    };

    return <>
        <Card
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

                {isLoggedIn && (
                    <Box sx={{ display: 'flex', gap: 1 }}>
                        {/* Show delete option if in collection view, show add options otherwise */}

                        {collectionId && isOwner && onRemove ? (
                            <Tooltip title="Remove from Collection">
                                <IconButton size="small" color="error" onClick={() => onRemove(media.mediaType, media.externalApiID)}>
                                    <DeleteIcon />
                                </IconButton>
                            </Tooltip>
                        ) : (
                            <>
                                <Tooltip title="Add to Collection">
                                    <IconButton size="small" color="primary" onClick={handleOpenMenu}>
                                        <PlaylistAddIcon />
                                    </IconButton>
                                </Tooltip>

                                <Tooltip title="Add to Favorites">
                                    <IconButton size="small" color="error" onClick={handleAddToFavorites}>
                                        <FavoriteBorderIcon />
                                    </IconButton>
                                </Tooltip>
                            </>
                        )}
                    </Box>
                )}
            </Box>
        </Card >

        {/* ADD TO COLLECTION DROPDOWN MENU */}
        <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={handleCloseMenu}>
            {isMenuLoading ? (
                <MenuItem disabled>
                    <CircularProgress size={20} sx={{ mr: 2 }} /> Loading...
                </MenuItem>
            ) : collections.length === 0 ? (
                <MenuItem disabled>No collections found.</MenuItem>
            ) : (
                collections.map(col => (
                    <MenuItem key={col.id} onClick={() => handleAddToCollection(col.id)}>
                        {col.name}
                    </MenuItem>
                ))
            )}
        </Menu>

        {/* TOAST NOTIFICATION */}
        <Snackbar
            open={toast.open}
            autoHideDuration={4000}
            onClose={() => setToast({ ...toast, open: false })}
            anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        >
            <Alert severity={toast.severity} variant="filled" sx={{ width: '100%' }}>
                {toast.message}
            </Alert>
        </Snackbar>
    </>
}