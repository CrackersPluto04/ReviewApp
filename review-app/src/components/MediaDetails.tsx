import { Avatar, Box, Card, CardContent, CardMedia, CircularProgress, Divider, Grid, Paper, Typography } from "@mui/material";
import { MediaDto } from "../types/types";
import { useState, useEffect } from "preact/hooks";
import { reviewService } from "../services/ReviewService";

type MediaDetailsProps = {
    media: MediaDto;
};

export function MediaDetails({ media }: MediaDetailsProps) {
    const [reviews, setReviews] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const fetchReviews = async () => {
            setLoading(true);
            const data = await reviewService.getMediaReviews(media.externalApiID, media.mediaType);
            setReviews(data);
            setLoading(false);
        };

        fetchReviews();
    }, [media]);

    return <Box sx={{ pb: 8 }}>
        {/* --- TOP SECTION: Media Info --- */}
        <Grid container spacing={4} sx={{ mb: 6 }}>
            {/* Left: Poster */}
            <Grid size={{ xs: 12, md: 3 }}>
                <Card elevation={4} sx={{ borderRadius: 3 }}>
                    <CardMedia
                        component="img"
                        image={media.posterUrl || 'https://placehold.co/300x450?text=No+Poster'}
                        alt={media.title}
                        sx={{ width: '100%', height: 'auto', aspectRatio: '2/3', objectFit: 'cover' }}
                    />
                </Card>
            </Grid>

            {/* Right: Details */}
            <Grid size={{ xs: 12, md: 9 }}>
                <Typography variant="h3" fontWeight="bold" gutterBottom>
                    {media.title}
                </Typography>

                <Typography variant="h6" color="text.secondary" gutterBottom>
                    {media.releaseDate ? media.releaseDate : 'Unknown Year'}
                </Typography>

                <Box sx={{ mt: 3 }}>
                    <Typography variant="h6" gutterBottom>
                        {media.mediaType === 2 ? 'Creator' : 'Overview'}
                    </Typography>
                    <Typography variant="body1" sx={{ lineHeight: 1.8 }}>
                        {media.mediaType === 2 ? media.creator || 'Unknown Creator' : media.overview || 'No overview available for this title.'}
                    </Typography>
                </Box>
            </Grid>
        </Grid>

        <Divider sx={{ mb: 4 }} />

        {/* --- BOTTOM SECTION: Reviews & Filters --- */}
        <Typography variant="h5" fontWeight="bold" sx={{ mb: 3 }}>
            Reviews ({reviews.length})
        </Typography>

        <Grid container spacing={4}>
            {/* Left: Review List */}
            <Grid size={{ xs: 12, md: 8 }}>
                {loading ? (
                    <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
                ) : reviews.length === 0 ? (
                    <Typography color="text.secondary">No public reviews yet. Be the first!</Typography>
                ) : (
                    reviews.map((rev) => (
                        <Card key={rev.username} variant="outlined" sx={{ mb: 3, display: 'flex', borderRadius: 2 }}>
                            {/* User Info Column */}
                            <Box sx={{ width: 140, p: 2, borderRight: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                                <Avatar sx={{ width: 56, height: 56, mb: 1 }} />
                                <Typography variant="subtitle2" noWrap>
                                    {rev.username}
                                </Typography>
                                <Typography variant="caption" color="text.secondary">
                                    {new Date(rev.createdAt).toLocaleDateString()}
                                </Typography>

                                <Typography variant="h6" fontWeight="bold" sx={{ mt: 2 }}>
                                    {rev.score}/10
                                </Typography>
                                <Typography variant="caption" color={rev.score >= 7 ? 'success.main' : rev.score >= 4 ? 'warning.main' : 'error.main'} fontWeight="bold">
                                    {rev.score >= 7 ? 'Great' : rev.score >= 4 ? 'Mixed' : 'Poor'}
                                </Typography>
                            </Box>

                            {/* Review Text Column */}
                            <CardContent sx={{ flexGrow: 1 }}>
                                <Typography variant="body1">
                                    {rev.reviewText || <span style={{ fontStyle: 'italic', color: 'gray' }}>No written review.</span>}
                                </Typography>

                                <Grid container spacing={2}>
                                    <Grid size={{ xs: 6 }}>
                                        <Typography variant="subtitle2" color="success.main" fontWeight="bold">Pros</Typography>
                                        <Typography variant="body2">{rev.pros || '-'}</Typography>
                                    </Grid>
                                    <Grid size={{ xs: 6 }}>
                                        <Typography variant="subtitle2" color="error.main" fontWeight="bold">Cons</Typography>
                                        <Typography variant="body2">{rev.cons || '-'}</Typography>
                                    </Grid>
                                </Grid>
                            </CardContent>
                        </Card>
                    ))
                )}
            </Grid>

            {/* Right: Sticky Filters */}
            <Grid size={{ xs: 12, md: 4 }}>
                <Paper
                    variant="outlined"
                    sx={{ p: 3, borderRadius: 2, position: 'sticky', top: 80 }}
                >
                    <Typography variant="h6" gutterBottom>Filter Reviews</Typography>
                    {/* Placeholder for real filters later! */}
                    <Box sx={{ height: 100, border: '1px dashed grey', borderRadius: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        Filters UI Placeholder
                    </Box>
                </Paper>
            </Grid>
        </Grid>
    </Box>
}