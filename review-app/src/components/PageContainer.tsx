import { Box, Container } from '@mui/material';
import { Header, HeaderProps } from './Header';
import { Footer } from './Footer';
import { EmailVerificationBanner } from './EmailVerificationBanner';
import { ReactNode } from 'preact/compat';

type PageContainerProps = HeaderProps & {
    children: ReactNode;
}

/**
 * The shared page chrome: header, the email verification banner above the page content, and the footer. Most
 * top-level routes are wrapped in it (see `AppRoutes`). It takes the header's props, so the theme toggle reaches it.
 */
export function PageContainer({ children, ...headerProps }: PageContainerProps) {
    return <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
        <Header {...headerProps} />

        <Box component="main" sx={{ flexGrow: 1, py: 4 }}>
            <Container disableGutters maxWidth="xl" sx={{ pr: 12, pl: 12 }}>
                <EmailVerificationBanner />
                {children}
            </Container>
        </Box>

        <Footer />
    </Box>
}