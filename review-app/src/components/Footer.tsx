import { Box, Typography, Container, Stack } from '@mui/material';

export function Footer() {
    return <Box sx={{ bgcolor: 'background.paper', py: 3, mt: 'auto', borderTop: 1, borderColor: 'divider' }}>
        <Container disableGutters maxWidth="xl" sx={{ pr: 12, pl: 12 }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3} justifyContent="space-between" alignItems="center">
                <Typography variant="h6" fontWeight="bold">
                    THE Review App
                </Typography>

                <Typography variant="body2" color="text.secondary">
                    © {new Date().getFullYear()} THE Review App. All rights reserved.
                </Typography>

                <Typography variant="body2" color="text.secondary">
                    Social Media Links Here
                </Typography>
            </Stack>
        </Container>
    </Box>
}