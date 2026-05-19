import { useEffect, useState } from "preact/hooks";
import { useParams, useSearchParams } from "react-router-dom";
import { userService } from "../services/UserService";
import { ReviewFilterParams } from "../types/types";
import { reviewService } from "../services/ReviewService";
import { Grid, Box, Typography, CircularProgress, Pagination } from "@mui/material";
import { ReviewCard } from "./ReviewCard";
import { ReviewFilter } from "./ReviewFilter";
import { MyPagination } from "./MyPagination";

export function ReviewsTab() {
    const { username } = useParams();
    const [searchParams, setSearchParams] = useSearchParams();

    // --- States ---
    const [reviews, setReviews] = useState<any[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [totalPages, setTotalPages] = useState(1);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    // --- Active States (From URL) ---
    const page = Number.parseInt(searchParams.get('page') || '1', 10);
    const sortBy = searchParams.get('sortBy') || 'created_desc';
    const minScore = searchParams.get('minScore') ? Number.parseFloat(searchParams.get('minScore')!) : 1;
    const maxScore = searchParams.get('maxScore') ? Number.parseFloat(searchParams.get('maxScore')!) : 10;
    const hasWrittenText = searchParams.get('hasWrittenText') === 'true';

    useEffect(() => {
        const fetchReviews = async () => {
            if (!username) return;
            setLoading(true);
            setErrorMessage('');

            const params: ReviewFilterParams = {
                page: page,
                sortBy: sortBy,
                minScore: minScore,
                maxScore: maxScore,
                hasWrittenText: hasWrittenText
            };
            const result = await userService.getUserReviews(username, params);

            if (result.success) {
                setReviews(result.data.items);
                setTotalCount(result.data.totalCount);
                setTotalPages(result.data.totalPages);
            } else
                setErrorMessage(result.message);

            setLoading(false);
        };

        fetchReviews();
    }, [username, page, sortBy, minScore, maxScore, hasWrittenText]);

    /* Handlers */
    const handlePageChange = (_event: any, newPage: number) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            newParams.set('page', newPage.toString());
            return newParams;
        });
    }

    const handleApplyFilter = (filters: any) => {
        const newParams = new URLSearchParams(searchParams);

        if (filters.sortBy) newParams.set('sortBy', filters.sortBy);
        if (filters.scoreRange[0] !== undefined) newParams.set('minScore', filters.scoreRange[0].toString());
        if (filters.scoreRange[1] !== undefined) newParams.set('maxScore', filters.scoreRange[1].toString());
        if (filters.hasWrittenText) newParams.set('hasWrittenText', 'true');
        else newParams.delete('hasWrittenText');

        newParams.set('page', '1');

        setSearchParams(newParams);
    };

    const handleClearFilter = () => {
        setSearchParams();
    };

    const handleDeleteReview = async (reviewId: number) => {
        if (!globalThis.confirm("Are you sure you want to delete this review? This cannot be undone!")) return;

        const result = await reviewService.deleteReview(reviewId);

        if (result.success) {
            setReviews(prev => prev.filter(r => r.id !== reviewId));
            setTotalCount(prev => prev - 1);
        } else
            alert(result.message || "Failed to delete review.");
    };

    return <Box>
        <Typography variant="h5" fontWeight="bold" sx={{ mb: 3 }}>
            Reviews ({totalCount})
        </Typography>

        {/* Pagination Top */}
        {totalPages > 1 && !loading &&
            <MyPagination page={page} totalPages={totalPages} onPageChange={handlePageChange} size="medium" mt={2} mb={2} />}

        {/* REVIEWS LIST AND FILTER */}
        <Grid container spacing={3}>
            {/* Left: The Reviews List */}
            <Grid size={{ xs: 12, md: 8 }}>
                {loading ? (
                    <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
                ) : reviews.length === 0 ? (
                    <Box sx={{ p: 5, textAlign: 'center', border: '1px dashed grey', borderRadius: 2, color: 'text.secondary' }}>
                        <Typography color={errorMessage ? "error" : "text.secondary"}>
                            {errorMessage || 'No reviews found matching these filters.'}
                        </Typography>
                    </Box>
                ) : (
                    reviews.map(rev => (
                        <ReviewCard
                            key={rev.id}
                            rev={rev}
                            mode="profile"
                            onDelete={handleDeleteReview}
                        />
                    ))
                )}
            </Grid>

            {/* Right: Sticky Filters */}
            <Grid size={{ xs: 12, md: 4 }}>
                <ReviewFilter
                    filters={{ sortBy, scoreRange: [minScore, maxScore], hasWrittenText }}
                    handleApply={handleApplyFilter}
                    handleClear={handleClearFilter}
                    disabled={reviews.length === 0 && loading && !!errorMessage}
                />
            </Grid>
        </Grid>

        {/* Pagination Bottom */}
        {totalPages > 1 && !loading &&
            <MyPagination page={page} totalPages={totalPages} onPageChange={handlePageChange} size="large" />}
    </Box>
}