import { Card, Box, Avatar, Typography, CardContent, Grid, Stack, IconButton, Chip, CardMedia, Tooltip } from "@mui/material";
import LockIcon from "@mui/icons-material/Lock"
import PublicIcon from "@mui/icons-material/Public"
import GroupIcon from "@mui/icons-material/Group"
import EditIcon from "@mui/icons-material/Edit"
import DeleteIcon from "@mui/icons-material/Delete"
import { useNavigate } from "react-router-dom";

type ReviewCardProps = {
    rev: any;
    mode?: 'media' | 'profile';
    onDelete?: (reviewId: number) => void;
}

export function ReviewCard({ rev, mode = 'media', onDelete }: ReviewCardProps) {
    const navigate = useNavigate();

    const handleUserClick = () => {
        navigate(`/profile/${rev.username}`);
    }

    const handleMediaClick = () => {
        navigate(`/media/${rev.mediaType}/${rev.externalApiID}?tab=details`);
    }

    const handleEditClick = () => {
        navigate(`/media/${rev.mediaType}/${rev.externalApiID}?tab=review`);
    };

    const renderVisibilityIcon = () => {
        if (rev.visibilityLevel === 0) return <Chip icon={<LockIcon />} label="Private" size="small" />;
        if (rev.visibilityLevel === 1) return <Chip icon={<PublicIcon />} label="Public" size="small" />;
        return <Chip icon={<GroupIcon />} label="Followers" size="small" />;
    };

    return <Card variant="outlined" sx={{ mb: 3, display: 'flex', borderRadius: 2 }}>
        {/* DYNAMIC LEFT COLUMN */}
        {mode === 'media' ? (
            // User infos
            <Box sx={{ width: 200, p: 2, borderRight: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                <IconButton onClick={handleUserClick} >
                    <Stack direction="row" spacing={2} alignItems="center" justifyContent="center">
                        <Avatar
                            alt="Profile Picture"
                            src={rev.profilePictureUrl}
                        />
                        <Typography variant="h6" noWrap fontWeight="bold">
                            {rev.username}
                        </Typography>
                    </Stack>
                </IconButton>

                <Typography variant="caption" color="text.secondary" sx={{ mt: 2 }}>
                    Created: {new Date(rev.createdAt).toLocaleDateString()}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                    Updated: {new Date(rev.updatedAt).toLocaleDateString()}
                </Typography>

                <Typography variant="h6" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold" sx={{ mt: 1 }}>
                    {rev.score}/10
                </Typography>
                <Typography variant="body1" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold">
                    {rev.score >= 7 ? 'Great' : rev.score >= 4 ? 'Mixed' : 'Poor'}
                </Typography>
            </Box>
        ) : (
            // Media Poster
            <Box sx={{ width: 140, minWidth: 140, borderRight: '1px solid', borderColor: 'divider' }}>
                <CardMedia
                    component="img"
                    onClick={handleMediaClick}
                    sx={{ height: '100%', objectFit: 'cover', cursor: 'pointer' }}
                    image={rev.posterUrl || 'https://placehold.co/140x200?text=No+Poster'}
                    alt={rev.title}
                />
            </Box>
        )}

        {/* RIGHT COLUMN */}
        <Box sx={{ flexGrow: 1, display: 'flex', flexDirection: 'column' }}>
            {/* Dynamic Header */}
            {mode === 'profile' &&
                <Box sx={{ p: 2, pb: 1, display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '1px solid', borderColor: 'divider' }}>
                    <Box>
                        <Typography variant="h6" fontWeight="bold" onClick={handleMediaClick} sx={{ cursor: 'pointer' }}>
                            {rev.title}
                        </Typography>

                        <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.5 }}>
                            <Typography variant="caption" color="text.secondary">
                                Reviews: {rev.createdAt}
                            </Typography>
                            <Typography variant="caption" color="text.secondary">•</Typography>
                            <Typography variant="caption" color="text.secondary">
                                Modified: {rev.updatedAt}
                            </Typography>

                            {rev.isOwner && (
                                <>
                                    <Typography variant="caption" color="text.secondary">•</Typography>
                                    {renderVisibilityIcon()}
                                </>
                            )}
                        </Stack>
                    </Box>

                    {/* Owner action buttons */}
                    {rev.isOwner && (
                        <Box>
                            <Tooltip title="Edit Review">
                                <IconButton size="small" color="primary" onClick={handleEditClick}>
                                    <EditIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                            <Tooltip title="Delete Review">
                                <IconButton size="small" color="error" onClick={() => onDelete?.(rev.id)}>
                                    <DeleteIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                        </Box>
                    )}
                </Box>
            }

            {/* Dynamic Score + Review text, pros, cons */}
            <CardContent sx={{ flexGrow: 1 }}>
                <Grid container spacing={2} mt={1}>

                    {/* LEFT: Score Column (Only in Profile Mode) */}
                    {mode === 'profile' && (
                        <Grid size={{ xs: 3, sm: 2 }}>
                            <Box sx={{
                                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                                height: '100%', borderRight: '2px dashed', borderColor: 'divider', pr: 2
                            }}>
                                <Typography variant="h6" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold">
                                    {rev.score}/10
                                </Typography>
                                <Typography variant="body1" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold">
                                    {rev.score >= 7 ? 'Great' : rev.score >= 4 ? 'Mixed' : 'Poor'}
                                </Typography>
                            </Box>
                        </Grid>
                    )}

                    {/* RIGHT: Main Text & Pros/Cons */}
                    <Grid size={{ xs: mode === 'profile' ? 9 : 12, sm: mode === 'profile' ? 10 : 12 }}>
                        <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                            {rev.reviewText || <span style={{ fontStyle: 'italic', color: 'gray' }}>No written review.</span>}
                        </Typography>

                        <Grid container spacing={2} mt={1}>
                            <Grid size={{ xs: 12, sm: 6 }}>
                                <Typography variant="subtitle2" color="success.main" fontWeight="bold">
                                    Pros
                                </Typography>
                                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                                    {rev.pros || '-'}
                                </Typography>
                            </Grid>
                            <Grid size={{ xs: 12, sm: 6 }}>
                                <Typography variant="subtitle2" color="error.main" fontWeight="bold">
                                    Cons
                                </Typography>
                                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                                    {rev.cons || '-'}
                                </Typography>
                            </Grid>
                        </Grid>
                    </Grid>

                </Grid>
            </CardContent>
        </Box>

    </Card >
}