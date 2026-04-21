import { Box, Card, CardMedia, CircularProgress, Divider, Grid, Paper, Typography } from "@mui/material";
import { MediaDto } from "../types/types";
import { useState, useEffect } from "preact/hooks";
import { reviewService } from "../services/ReviewService";
import { ReviewCard } from "./ReviewCard";

type MediaDetailsProps = {
    media: MediaDto;
};

export function MediaDetails({ media }: MediaDetailsProps) {
    const [reviews, setReviews] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    useEffect(() => {
        const fetchReviews = async () => {
            setLoading(true);

            const result = await reviewService.getMediaReviews(media.externalApiID, media.mediaType);
            if (result.success)
                setReviews(result.data);
            else
                setErrorMessage(result.message)

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
                    <Typography color={errorMessage ? "error" : "text.secondary"}>
                        {errorMessage || 'No public reviews yet. Be the first!'}
                    </Typography>
                ) : (
                    reviews.map((rev) => (
                        <ReviewCard key={rev.username} rev={rev} />
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