import { useEffect, useState } from "preact/hooks";
import { Alert, Avatar, Box, Button, CircularProgress, Stack, Typography } from "@mui/material";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { reviewService } from "../services/ReviewService";
import { reviewReplyService } from "../services/ReviewReplyService";
import { ReviewCard } from "../components/ReviewCard";
import { ReplyFocus } from "../components/ReplyThread";

// A single review on its own URL, the target of reply notifications (?reply=<id> opens the thread at that reply)
export function ReviewPage() {
    const { reviewId } = useParams();
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();

    const id = Number.parseInt(reviewId ?? '', 10);
    const replyId = Number.parseInt(searchParams.get('reply') ?? '', 10);

    const [review, setReview] = useState<any>(null);
    const [replyFocus, setReplyFocus] = useState<ReplyFocus>();
    const [replyMissing, setReplyMissing] = useState(false);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    useEffect(() => {
        const load = async () => {
            setLoading(true);
            setErrorMessage('');
            setReview(null);
            setReplyFocus(undefined);
            setReplyMissing(false);

            if (Number.isNaN(id)) {
                setErrorMessage("This review doesn't exist or isn't visible to you.");
                setLoading(false);
                return;
            }

            const [reviewResult, replyResult] = await Promise.all([
                reviewService.getReview(id),
                Number.isNaN(replyId) ? Promise.resolve(null) : reviewReplyService.getReplyContext(replyId)
            ]);

            if (!reviewResult.success) {
                setErrorMessage(reviewResult.notFound
                    ? "This review doesn't exist or isn't visible to you."
                    : reviewResult.message ?? 'Something went wrong.');
                setLoading(false);
                return;
            }

            setReview(reviewResult.data);

            if (replyResult) {
                if (replyResult.success && replyResult.data?.reviewID === id)
                    setReplyFocus({ reply: replyResult.data.reply, parentReply: replyResult.data.parentReply });
                else
                    setReplyMissing(true);
            }

            setLoading(false);
        };

        load();
    }, [id, replyId]);

    const handleDelete = async (deletedId: number) => {
        if (!globalThis.confirm("Are you sure you want to delete this review? This cannot be undone!")) return;

        const result = await reviewService.deleteReview(deletedId);
        if (result.success)
            navigate(`/profile/${encodeURIComponent(review.username)}/reviews`, { replace: true });
        else
            alert(result.message || "Failed to delete review.");
    };

    if (loading)
        return <Box p={5} display="flex" justifyContent="center"><CircularProgress /></Box>;

    if (errorMessage || !review)
        return <Box sx={{ p: 5, textAlign: 'center', border: '1px dashed grey', borderRadius: 2 }}>
            <Typography color="text.secondary" gutterBottom>{errorMessage}</Typography>
            <Button onClick={() => navigate('/home')}>Go to the home page</Button>
        </Box>;

    return <Box sx={{ maxWidth: 900, mx: 'auto' }}>
        {/* Author */}
        <Stack direction="row" spacing={1.5} alignItems="center" sx={{ mb: 2, cursor: 'pointer', width: 'fit-content' }}
            onClick={() => navigate(`/profile/${encodeURIComponent(review.username)}`)}
        >
            <Avatar src={review.profilePictureUrl} alt={review.username} />
            <Typography variant="h6">
                <Box component="span" fontWeight="bold">{review.username}</Box>'s review
            </Typography>
        </Stack>

        {replyMissing && (
            <Alert severity="info" sx={{ mb: 2 }}>
                The reply you were looking for was deleted or is no longer available.
            </Alert>
        )}

        <ReviewCard rev={review} mode="profile" onDelete={handleDelete} replyFocus={replyFocus} />
    </Box>
}
