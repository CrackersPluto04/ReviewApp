import { Card, Box, Avatar, Typography, CardContent, Grid } from "@mui/material";

type ReviewCardProps = {
    rev: any;
}

export function ReviewCard({ rev }: ReviewCardProps) {
    return <Card variant="outlined" sx={{ mb: 3, display: 'flex', borderRadius: 2 }}>
        {/* User Info Column */}
        <Box sx={{ width: 140, p: 2, borderRight: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <Avatar
                src={rev.profilePictureUrl}
                sx={{ width: 56, height: 56, mb: 1 }}
            />

            <Typography variant="subtitle2" noWrap>
                {rev.username}
            </Typography>
            <Typography variant="caption" color="text.secondary">
                {new Date(rev.createdAt).toLocaleDateString()}
            </Typography>

            <Typography variant="h6" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold" sx={{ mt: 2 }}>
                {rev.score}/10
            </Typography>
            <Typography variant="caption" color={rev.score >= 7 ? 'success' : rev.score >= 4 ? 'warning' : 'error'} fontWeight="bold">
                {rev.score >= 7 ? 'Great' : rev.score >= 4 ? 'Mixed' : 'Poor'}
            </Typography>
        </Box>

        {/* Review Text Column */}
        <CardContent sx={{ flexGrow: 1 }}>
            <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                {rev.reviewText || <span style={{ fontStyle: 'italic', color: 'gray' }}>No written review.</span>}
            </Typography>

            <Grid container spacing={2} mt={1}>
                <Grid size={{ xs: 6 }}>
                    <Typography variant="subtitle2" color="success.main" fontWeight="bold">
                        Pros
                    </Typography>
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                        {rev.pros || '-'}
                    </Typography>
                </Grid>
                <Grid size={{ xs: 6 }}>
                    <Typography variant="subtitle2" color="error.main" fontWeight="bold">
                        Cons
                    </Typography>
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                        {rev.cons || '-'}
                    </Typography>
                </Grid>
            </Grid>
        </CardContent>
    </Card>
}