import { useRef, useState } from "preact/hooks";
import { Box, Button, FormHelperText, Stack, TextField, Typography } from "@mui/material";
import { reviewReplyService } from "../services/ReviewReplyService";
import { ReplyDto } from "../types/types";
import { MAX_REPLY_LENGTH, isValidReply } from "../utils/validation";

type ReplyFormProps = {
    reviewId: number;
    /** Omit to reply to the review itself; the layer-2 reply's ID to reply to it. */
    parentReplyId?: number;
    /** The username shown in the placeholder ("Reply to ..."). */
    replyingTo?: string | null;
    /** Receives the created reply, which the parent adds to its list. */
    onCreated: (reply: ReplyDto) => void;
    /** Shows a Cancel button when given. */
    onCancel?: () => void;
    autoFocus?: boolean;
};

/**
 * The form to post a reply. The text is validated with `isValidReply` (trimmed, at most `MAX_REPLY_LENGTH`), a
 * double submit is blocked, and after a failure the draft is kept so the user can retry.
 */
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
        {/* Buttons sit next to the field, so they don't take up a separate row */}
        <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1 }}>
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
                disabled={submitting}
            />

            <Stack spacing={0.5} sx={{ flexShrink: 0 }}>
                <Button type="submit" size="small" variant="contained" disabled={submitting || !isValidReply(content)}>
                    {submitting ? 'Posting...' : 'Reply'}
                </Button>
                {onCancel && (
                    <Button size="small" onClick={onCancel} disabled={submitting}>
                        Cancel
                    </Button>
                )}
            </Stack>
        </Box>

        {/* Counter is kept outside the TextField (not helperText), so the buttons line up with the field's bottom edge */}
        <FormHelperText error={tooLong} sx={{ mx: 1.75 }}>
            {content.trim().length}/{MAX_REPLY_LENGTH}
        </FormHelperText>

        {errorMessage && (
            <Typography variant="body2" color="error" sx={{ mt: 0.5 }}>
                {errorMessage}
            </Typography>
        )}
    </Box>
}
