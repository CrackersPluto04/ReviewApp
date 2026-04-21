import { Alert, Box, Button, Card, CardMedia, CircularProgress, FormControl, Grid, InputLabel, MenuItem, Select, Slider, Snackbar, TextField, Typography } from "@mui/material";
import { MediaDto } from "../types/types";
import { useEffect, useState } from "preact/hooks";
import { reviewService } from "../services/ReviewService";

type WriteEditReviewProps = {
    media: MediaDto;
};

export function WriteEditReview({ media }: WriteEditReviewProps) {
    const [loading, setLoading] = useState(true);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [isEditing, setIsEditing] = useState(false);
    const [toastMessage, setToastMessage] = useState('');
    const [error, setError] = useState(false);

    // Form states
    const [score, setScore] = useState<number>(5);
    const [reviewText, setReviewText] = useState('');
    const [pros, setPros] = useState('');
    const [cons, setCons] = useState('');
    const [visibility, setVisibility] = useState(1);

    // Text length max limits
    const MAX_REVIEW = 500;
    const MAX_PROS_CONS = 250;

    // Length validation
    const isReviewOver = reviewText.length > MAX_REVIEW;
    const isProsOver = pros.length > MAX_PROS_CONS;
    const isConsOver = cons.length > MAX_PROS_CONS;

    const hasErrors = isReviewOver || isProsOver || isConsOver;

    useEffect(() => {
        const checkExistingReview = async () => {
            setLoading(true);

            const result = await reviewService.checkIfUserReviewedMedia(media.externalApiID, media.mediaType);
            if (result.success) {
                if (result.data.hasReviewed && result.data.reviewData) {
                    setIsEditing(true);
                    setScore(result.data.reviewData.score);
                    setReviewText(result.data.reviewData.reviewText || '');
                    setPros(result.data.reviewData.pros || '');
                    setCons(result.data.reviewData.cons || '');
                    setVisibility(result.data.reviewData.visibilityLevel);
                }
            } else {
                setError(true);
                setToastMessage(result.message)
            }

            setLoading(false);
        };

        checkExistingReview();
    }, [media]);

    const handleSubmit = async () => {
        setIsSubmitting(true);

        const payload = {
            mediaDto: media,
            reviewDto: { score, reviewText, pros, cons, visibilityLevel: visibility }
        };

        if (isEditing) {
            const result = await reviewService.editReview(payload);
            if (result.success)
                setToastMessage(result.data.message);
        } else {
            const result = await reviewService.createReview(payload);
            if (result.success)
                setToastMessage(result.data.message);
            else {
                setError(true);
                setToastMessage(result.message)
            }
            setIsEditing(true);
        }

        setIsSubmitting(false);
    };

    if (loading) {
        return <Box display="flex" justifyContent="center" p={10}><CircularProgress /></Box>;
    }

    return <Box sx={{ pb: 8 }}>
        <Grid container spacing={6}>
            {/* --- LEFT SIDE: The Review Form --- */}
            <Grid size={{ xs: 12, md: 8 }}>
                <Typography variant="h4" fontWeight="bold" gutterBottom>
                    {isEditing ? 'Edit your Review' : 'Write your Review'}
                </Typography>

                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 4, mt: 4 }}>

                    {/* Score Slider */}
                    <Box>
                        <Typography variant="h6" gutterBottom>
                            Your Score: <Box component="span" sx={{ fontWeight: 'bold', color: 'primary.main' }}>{score} / 10</Box>
                        </Typography>
                        <Slider
                            value={score}
                            onChange={(_, val) => setScore(val)}
                            step={0.5} min={1} max={10} valueLabelDisplay="auto"
                            color={score >= 7 ? 'success' : score >= 4 ? 'warning' : 'error'}
                            sx={{ maxWidth: 400 }}
                        />
                    </Box>

                    {/* Text Fields */}
                    <TextField
                        label="What did you think?"
                        multiline rows={6}
                        value={reviewText}
                        onChange={e => setReviewText(e.currentTarget.value)}
                        fullWidth
                        placeholder="Write your review here..."
                        error={isReviewOver}
                        helperText={<Box component="span" sx={{ float: 'right' }}>{reviewText.length} / {MAX_REVIEW}</Box>}
                    />

                    <Grid container spacing={3}>
                        <Grid size={{ xs: 12, sm: 6 }}>
                            <TextField
                                label="Pros"
                                multiline rows={2}
                                value={pros}
                                onChange={e => setPros(e.currentTarget.value)}
                                fullWidth
                                error={isProsOver}
                                helperText={<Box component="span" sx={{ float: 'right' }}>{pros.length} / {MAX_PROS_CONS}</Box>}
                            />
                        </Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>
                            <TextField
                                label="Cons"
                                multiline rows={2}
                                value={cons}
                                onChange={e => setCons(e.currentTarget.value)}
                                fullWidth
                                error={isConsOver}
                                helperText={<Box component="span" sx={{ float: 'right' }}>{cons.length} / {MAX_PROS_CONS}</Box>}
                            />
                        </Grid>
                    </Grid>

                    {/* Visibility & Submit */}
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: 2 }}>
                        <FormControl sx={{ minWidth: 200 }}>
                            <InputLabel>Visibility</InputLabel>
                            <Select value={visibility} label="Visibility" onChange={(e) => setVisibility(Number((e.target as HTMLInputElement).value))}>
                                <MenuItem value={0}>Private</MenuItem>
                                <MenuItem value={1}>Public</MenuItem>
                                <MenuItem value={2}>Followers only</MenuItem>
                            </Select>
                        </FormControl>

                        <Button
                            variant="contained"
                            size="large"
                            onClick={handleSubmit}
                            disabled={isSubmitting || hasErrors}
                            sx={{ px: 5, py: 1.5, borderRadius: 2 }}
                        >
                            {isSubmitting ? <CircularProgress size={24} color="inherit" /> : (isEditing ? 'Update Review' : 'Save Review')}
                        </Button>
                    </Box>
                </Box>
            </Grid>

            {/* --- RIGHT SIDE: The Media Details --- */}
            <Grid size={{ xs: 12, md: 4 }}>
                <Box sx={{ position: 'sticky', top: 80 }}>
                    <Card elevation={0} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 3, p: 2, textAlign: 'center' }}>
                        <Typography variant="h6" fontWeight="bold">
                            {media.title}
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                            {media.releaseDate ? media.releaseDate : 'Unknown Year'}
                        </Typography>
                        <CardMedia
                            component="img"
                            image={media.posterUrl || 'https://placehold.co/300x450?text=No+Poster'}
                            alt={media.title}
                            sx={{ width: '100%', maxWidth: 250, height: 'auto', aspectRatio: '2/3', objectFit: 'cover', borderRadius: 2, mx: 'auto', mb: 2 }}
                        />
                    </Card>
                </Box>
            </Grid>

        </Grid>

        {/* Popup after successfull save or occuring error! */}
        <Snackbar open={!!toastMessage} autoHideDuration={3000} onClose={() => setToastMessage('')}>
            <Alert severity={error ? "error" : "success"} sx={{ width: '100%' }}>
                {toastMessage}
            </Alert>
        </Snackbar>
    </Box>
}