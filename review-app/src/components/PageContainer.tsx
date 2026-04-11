import { Box, Container } from '@mui/material';
import { Header, HeaderProps } from './Header';
import { Footer } from './Footer';
import { ReactNode } from 'preact/compat';

type PageContainerProps = HeaderProps & {
    children: ReactNode;
}

export function PageContainer({ children, ...headerProps }: PageContainerProps) {
    return <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
        <Header {...headerProps} />

        <Box component="main" sx={{ flexGrow: 1, py: 4 }}>
            <Container disableGutters maxWidth="xl" sx={{ pr: 12, pl: 12 }}>
                {children}
            </Container>
        </Box>

        <Footer />
    </Box>
}