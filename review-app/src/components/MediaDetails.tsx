import { Box, Card, CardMedia, CircularProgress, Divider, Grid, Typography } from "@mui/material";
import { MediaDto, ReviewFilterParams } from "../types/types";
import { useState, useEffect } from "preact/hooks";
import { reviewService } from "../services/ReviewService";
import { ReviewCard } from "./ReviewCard";
import { MyPagination } from "./MyPagination";
import { useLocation, useSearchParams } from "react-router-dom";
import { ReviewFilter } from "./ReviewFilter";

type MediaDetailsProps = {
    media: MediaDto;
};

/**
 * The details tab of a title: poster, release date, overview (or the artists for music) and the title's public
 * reviews, paged, with a sort and filter panel. The applied sort, score range, written-only switch and page live
 * in the URL, so a filtered view can be linked and survives a reload. The router state that carries the title
 * is passed along on every URL change, otherwise the media page would have to fetch the title again.
 */
export function MediaDetails({ media }: MediaDetailsProps) {
    const [searchParams, setSearchParams] = useSearchParams();
    const location = useLocation();

    const [reviews, setReviews] = useState<any[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [totalPages, setTotalPages] = useState(1);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    const disabled = reviews.length === 0 || loading || !!errorMessage;

    const pageUrl = Number.parseInt(searchParams.get('page') || '1', 10);
    const sortByUrl = searchParams.get('sortBy') || 'created_desc';
    const minScoreUrl = searchParams.get('minScore') ? Number.parseFloat(searchParams.get('minScore')!) : 1;
    const maxScoreUrl = searchParams.get('maxScore') ? Number.parseFloat(searchParams.get('maxScore')!) : 10;
    const hasWrittenTextUrl = searchParams.get('hasWrittenText') === 'true';

    const scoreRange = [minScoreUrl, maxScoreUrl];

    const [draftFilters, setDraftFilters] = useState({
        sortBy: sortByUrl,
        scoreRange: scoreRange,
        hasWrittenText: hasWrittenTextUrl
    });

    useEffect(() => {
        setDraftFilters({
            sortBy: sortByUrl,
            scoreRange: scoreRange,
            hasWrittenText: hasWrittenTextUrl
        });
    }, [sortByUrl, minScoreUrl, maxScoreUrl, hasWrittenTextUrl]);

    useEffect(() => {
        const fetchReviews = async () => {
            setLoading(true);
            setErrorMessage('');

            const params: ReviewFilterParams = {
                mediaType: media.mediaType,
                externalApiId: media.externalApiID,
                page: pageUrl,
                sortBy: sortByUrl,
                minScore: minScoreUrl,
                maxScore: maxScoreUrl,
                hasWrittenText: hasWrittenTextUrl
            };
            const result = await reviewService.getMediaReviews(params);

            if (result.success) {
                setReviews(result.data.items);
                setTotalCount(result.data.totalCount);
                setTotalPages(result.data.totalPages);
            }
            else
                setErrorMessage(result.message)

            setLoading(false);
        };

        fetchReviews();
    }, [media, pageUrl, sortByUrl, minScoreUrl, maxScoreUrl, hasWrittenTextUrl]);

    const handlePageChange = (_event: any, newPage: number) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            newParams.set('page', newPage.toString());
            return newParams;
        }, { state: location.state });
    };

    const handleApply = (filters: any) => {
        const newParams = new URLSearchParams();

        if (filters.sortBy) newParams.set('sortBy', filters.sortBy);
        if (filters.scoreRange[0] !== undefined) newParams.set('minScore', filters.scoreRange[0].toString());
        if (filters.scoreRange[1] !== undefined) newParams.set('maxScore', filters.scoreRange[1].toString());
        if (filters.hasWrittenText) newParams.set('hasWrittenText', 'true');

        newParams.set('page', '1');

        setSearchParams(newParams, { state: location.state });
    };

    const handleClear = () => {
        setDraftFilters({
            sortBy: 'created_desc',
            scoreRange: [1, 10],
            hasWrittenText: false
        });
        setSearchParams({ 'tab': 'details' }, { state: location.state });
    }

    return <Box sx={{ pb: 8 }}>
        <Grid container spacing={4} sx={{ mb: 6 }}>
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

        <Typography variant="h5" fontWeight="bold" sx={{ mb: 3 }}>
            Reviews ({totalCount})
        </Typography>

        {totalPages > 1 && !loading &&
            <MyPagination page={pageUrl} totalPages={totalPages} onPageChange={handlePageChange} size="medium" mt={2} mb={2} />}

        <Grid container spacing={4}>
            <Grid size={{ xs: 12, md: 8 }}>
                {loading ? (
                    <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
                ) : reviews.length === 0 ? (
                    <Typography color={errorMessage ? "error" : "text.secondary"}>
                        {errorMessage || 'No public reviews yet. Be the first!'}
                    </Typography>
                ) : (
                    reviews.map((rev) => (
                        <ReviewCard key={rev.id} rev={rev} />
                    ))
                )}
            </Grid>

            <Grid size={{ xs: 12, md: 4 }}>
                <ReviewFilter
                    filters={draftFilters}
                    handleApply={handleApply}
                    handleClear={handleClear}
                    disabled={disabled}
                />
            </Grid>
        </Grid>

        {totalPages > 1 && !loading &&
            <MyPagination page={pageUrl} totalPages={totalPages} onPageChange={handlePageChange} size="large" />}
    </Box>
}
