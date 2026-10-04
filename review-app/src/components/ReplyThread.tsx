import { useState } from "preact/hooks";
import { Box, Button, CircularProgress, Collapse, Typography } from "@mui/material";
import ChatBubbleOutlineIcon from "@mui/icons-material/ChatBubbleOutline";
import { useAuth } from "../context/AuthContext";
import { useReplyList } from "../hooks/useReplyList";
import { ReplyDto } from "../types/types";
import { ReplyForm } from "./ReplyForm";
import { ReplyItem } from "./ReplyItem";
import { PleaseLogin } from "./PleaseLogin";

type ReplyThreadProps = {
    reviewId: number;
    replyCount: number;
    replyable: boolean; // false for private reviews
    onCountChange: (delta: number) => void;
};

export function ReplyThread({ reviewId, replyCount, replyable, onCountChange }: ReplyThreadProps) {
    const { isLoggedIn } = useAuth();
    const replies = useReplyList(reviewId);
    const [expanded, setExpanded] = useState(false);

    const canReply = isLoggedIn && replyable;

    // Replies are only loaded once the thread is opened
    const handleToggle = () => {
        if (!expanded && !replies.loaded) replies.loadMore();
        setExpanded(prev => !prev);
    };

    const handleCreated = (reply: ReplyDto) => {
        replies.addReply(reply);
        onCountChange(1);
    };

    const handleDeleted = (replyId: number, keepAsPlaceholder: boolean) => {
        if (keepAsPlaceholder)
            replies.markDeleted(replyId);
        else
            replies.removeReply(replyId);
        onCountChange(-1);
    };

    return <Box>
        <Button size="small" startIcon={<ChatBubbleOutlineIcon fontSize="small" />} onClick={handleToggle}>
            {expanded ? 'Hide replies' : `Replies (${replyCount})`}
        </Button>

        {/* Collapse keeps the replies mounted, so their state (child counts, drafts) survives hiding the thread */}
        <Collapse in={expanded}>
            <Box sx={{ mt: 1 }}>
                {canReply ? (
                    <ReplyForm reviewId={reviewId} onCreated={handleCreated} />
                ) : !replyable ? (
                    <Typography variant="body2" color="text.secondary" sx={{ py: 1 }}>
                        Private reviews can't be replied to.
                    </Typography>
                ) : (
                    <PleaseLogin compact message="Log in to join the conversation." />
                )}

                {replies.replies.map(reply => (
                    <ReplyItem
                        key={reply.id}
                        reply={reply}
                        reviewId={reviewId}
                        layer={2}
                        canReply={canReply}
                        onDeleted={handleDeleted}
                        onEmptied={replies.removeReply}
                        onCountChange={onCountChange}
                    />
                ))}

                {replies.loading && (
                    <Box display="flex" justifyContent="center" py={1}><CircularProgress size={24} /></Box>
                )}

                {replies.errorMessage && (
                    <Typography variant="body2" color="error" sx={{ py: 1 }}>{replies.errorMessage}</Typography>
                )}

                {replies.loaded && !replies.loading && replies.replies.length === 0 && !replies.errorMessage && (
                    <Typography variant="body2" color="text.secondary" sx={{ py: 1 }}>
                        No replies yet.
                    </Typography>
                )}

                {replies.hasMore && !replies.loading && (
                    <Button size="small" onClick={replies.loadMore}>
                        Load more replies
                    </Button>
                )}
            </Box>
        </Collapse>
    </Box>
}
