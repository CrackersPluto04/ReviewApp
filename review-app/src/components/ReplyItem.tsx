import { useState } from "preact/hooks";
import { Avatar, Box, Button, CircularProgress, IconButton, Stack, Tooltip, Typography } from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import KeyboardArrowDownIcon from "@mui/icons-material/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@mui/icons-material/KeyboardArrowUp";
import { useNavigate } from "react-router-dom";
import { ReplyDto } from "../types/types";
import { reviewReplyService } from "../services/ReviewReplyService";
import { useReplyList } from "../hooks/useReplyList";
import { ReplyForm } from "./ReplyForm";

type ReplyItemProps = {
    reply: ReplyDto;
    reviewId: number;
    layer: 2 | 3; // Layer 1 is the review itself, layer 3 replies can't be replied to
    canReply: boolean;
    // keepAsPlaceholder: the reply still has children, so it stays as a "deleted" placeholder
    onDeleted: (replyId: number, keepAsPlaceholder: boolean) => void;
    // A deleted placeholder lost its last child and should disappear
    onEmptied?: (replyId: number) => void;
    onCountChange: (delta: number) => void;
};

export function ReplyItem({ reply, reviewId, layer, canReply, onDeleted, onEmptied, onCountChange }: ReplyItemProps) {
    const navigate = useNavigate();
    const children = useReplyList(reviewId, reply.id);

    const [childCount, setChildCount] = useState(reply.childCount);
    const [showChildren, setShowChildren] = useState(false);
    const [showForm, setShowForm] = useState(false);
    const [deleting, setDeleting] = useState(false);

    const handleUserClick = () => {
        if (reply.username) navigate(`/profile/${encodeURIComponent(reply.username)}`);
    };

    const handleToggleChildren = () => {
        if (!showChildren && !children.loaded) children.loadMore();
        setShowChildren(prev => !prev);
    };

    const handleDelete = async () => {
        if (!globalThis.confirm("Are you sure you want to delete this reply? This cannot be undone!")) return;

        setDeleting(true);
        const result = await reviewReplyService.deleteReply(reply.id);
        setDeleting(false);

        if (result.success)
            onDeleted(reply.id, layer === 2 && childCount > 0);
        else
            alert(result.message || "Failed to delete reply.");
    };

    const handleChildCreated = (child: ReplyDto) => {
        setShowForm(false);
        setShowChildren(true);
        children.addReply(child);
        if (!children.loaded) children.loadMore();

        setChildCount(prev => prev + 1);
        onCountChange(1);
    };

    const handleChildDeleted = (childId: number) => {
        // Layer 3 replies have no children, so they are always removed
        children.removeReply(childId);
        onCountChange(-1);

        const remaining = childCount - 1;
        setChildCount(remaining);
        if (reply.isDeleted && remaining === 0) onEmptied?.(reply.id);
    };

    return <Box sx={{ display: 'flex', gap: 1.5, py: 1 }}>
        {reply.isDeleted ? (
            <Avatar sx={{ width: 32, height: 32 }} />
        ) : (
            <Avatar
                alt={reply.username ?? undefined}
                src={reply.profilePictureUrl ?? undefined}
                onClick={handleUserClick}
                sx={{ width: 32, height: 32, cursor: 'pointer' }}
            />
        )}

        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            {/* Header: author, date, owner actions */}
            <Stack direction="row" alignItems="center" spacing={1}>
                {reply.isDeleted ? (
                    <Typography variant="body2" color="text.secondary" fontStyle="italic">
                        This reply was deleted
                    </Typography>
                ) : (
                    <>
                        <Typography variant="subtitle2" fontWeight="bold" onClick={handleUserClick} sx={{ cursor: 'pointer' }}>
                            {reply.username}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                            {new Date(reply.createdAt).toLocaleString()}
                        </Typography>
                    </>
                )}

                <Box sx={{ flexGrow: 1 }} />

                {reply.isOwner && (
                    <Tooltip title="Delete Reply">
                        <span>
                            <IconButton size="small" color="error" aria-label="Delete reply" onClick={handleDelete} disabled={deleting}>
                                <DeleteIcon fontSize="small" />
                            </IconButton>
                        </span>
                    </Tooltip>
                )}
            </Stack>

            {/* Plain text only, never rendered as HTML */}
            {!reply.isDeleted && (
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                    {reply.content}
                </Typography>
            )}

            {/* Actions (only layer 2 replies can be replied to) */}
            {layer === 2 && (
                <Stack direction="row" spacing={1} sx={{ mt: 0.5 }}>
                    {canReply && (
                        <Button size="small" onClick={() => setShowForm(prev => !prev)}>
                            Reply
                        </Button>
                    )}
                    {childCount > 0 && (
                        <Button size="small" startIcon={showChildren ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />} onClick={handleToggleChildren}>
                            {showChildren ? 'Hide replies' : `View ${childCount} ${childCount === 1 ? 'reply' : 'replies'}`}
                        </Button>
                    )}
                </Stack>
            )}

            {layer === 2 && showForm && canReply && (
                <ReplyForm
                    reviewId={reviewId}
                    parentReplyId={reply.id}
                    replyingTo={reply.username}
                    onCreated={handleChildCreated}
                    onCancel={() => setShowForm(false)}
                    autoFocus
                />
            )}

            {/* Layer 3 replies */}
            {layer === 2 && showChildren && (
                <Box sx={{ borderLeft: '2px solid', borderColor: 'divider', pl: 2, mt: 0.5 }}>
                    {children.replies.map(child => (
                        <ReplyItem
                            key={child.id}
                            reply={child}
                            reviewId={reviewId}
                            layer={3}
                            canReply={false}
                            onDeleted={handleChildDeleted}
                            onCountChange={onCountChange}
                        />
                    ))}

                    {children.loading && <CircularProgress size={20} sx={{ my: 1 }} />}

                    {children.errorMessage && (
                        <Typography variant="body2" color="error">{children.errorMessage}</Typography>
                    )}

                    {children.hasMore && !children.loading && (
                        <Button size="small" onClick={children.loadMore}>
                            Load more replies
                        </Button>
                    )}
                </Box>
            )}
        </Box>
    </Box>
}
