import { useRef, useState } from "preact/hooks";
import { Box, Button, TextField, Typography } from "@mui/material";
import { reviewReplyService } from "../services/ReviewReplyService";
import { ReplyDto } from "../types/types";
import { MAX_REPLY_LENGTH, isValidReply } from "../utils/validation";

type ReplyFormProps = {
    reviewId: number;
    parentReplyId?: number;
    replyingTo?: string | null; // Username shown in the placeholder
    onCreated: (reply: ReplyDto) => void;
    onCancel?: () => void;
    autoFocus?: boolean;
};

export function ReplyForm({ reviewId, parentReplyId, replyingTo, onCreated, onCancel, autoFocus = false }: ReplyFormProps) {
    const [content, setContent] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [errorMessage, setErrorMessage] = useState('');
    // Guards against double submits before the disabled state re-renders
    const submittingRef = useRef(false);

    const tooLong = content.trim().length > MAX_REPLY_LENGTH;

    const handleSubmit = async (event: Event) => {
        event.preventDefault();
        if (submittingRef.current) return;

        if (!isValidReply(content)) {
            setErrorMessage(tooLong ? `Reply can be at most ${MAX_REPLY_LENGTH} characters.` : 'Reply cannot be empty.');
            return;
        }

        submittingRef.current = true;
        setSubmitting(true);
        setErrorMessage('');

        const result = await reviewReplyService.createReply({
            reviewID: reviewId,
            parentReplyID: parentReplyId ?? null,
            content: content.trim()
        });

        if (result.success) {
            setContent('');
            onCreated(result.data);
        } else
            // Draft is kept so the user can retry
            setErrorMessage(result.message);

        submittingRef.current = false;
        setSubmitting(false);
    };

    return <Box component="form" onSubmit={handleSubmit} sx={{ mt: 1, mb: 1 }}>
        <TextField
            fullWidth
            multiline
            minRows={2}
            size="small"
            autoFocus={autoFocus}
            placeholder={replyingTo ? `Reply to ${replyingTo}...` : 'Write a reply...'}
            value={content}
            onChange={(e: any) => setContent(e.target.value)}
            error={tooLong}
            helperText={`${content.trim().length}/${MAX_REPLY_LENGTH}`}
            disabled={submitting}
        />

        {errorMessage && (
            <Typography variant="body2" color="error" sx={{ mt: 0.5 }}>
                {errorMessage}
            </Typography>
        )}

        <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1, mt: 0.5 }}>
            {onCancel && (
                <Button size="small" onClick={onCancel} disabled={submitting}>
                    Cancel
                </Button>
            )}
            <Button type="submit" size="small" variant="contained" disabled={submitting || !isValidReply(content)}>
                {submitting ? 'Posting...' : 'Reply'}
            </Button>
        </Box>
    </Box>
}
