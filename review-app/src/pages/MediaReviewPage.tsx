import { useState } from 'preact/hooks';
import { useLocation, Navigate } from 'react-router-dom';
import { Box, Tabs, Tab } from '@mui/material';
import { MediaDetails } from '../components/MediaDetails';
import { WriteEditReview } from '../components/WriteEditReview';
import { PleaseLogin } from '../components/PleaseLogin';
import { MediaDto } from '../types/types';

export function MediaReviewPage() {
    const location = useLocation();
    const media = location.state?.media as MediaDto;

    const [activeTab, setActiveTab] = useState<'details' | 'review'>('details');
    const isLoggedIn = !!localStorage.getItem('jwt_token');

    if (!media) {
        return <Navigate to="/search" replace />;
    }

    return <Box sx={{ mt: 2 }}>
        <Tabs value={activeTab} onChange={(_, newValue) => setActiveTab(newValue)} centered sx={{ mb: 4 }}>
            <Tab label="Details" value="details" />
            <Tab label="Write/Edit Review" value="review" />
        </Tabs>

        {activeTab === 'details' && <MediaDetails media={media} />}

        {activeTab === 'review' && (
            isLoggedIn
                ? <WriteEditReview media={media} />
                : <PleaseLogin message={`Join the community to log your review for ${media.title}!`} />
        )}
    </Box>
}