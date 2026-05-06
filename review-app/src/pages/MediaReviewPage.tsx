import { useLocation, Navigate, useParams, useSearchParams } from 'react-router-dom';
import { Box, Tabs, Tab } from '@mui/material';
import { MediaDetails } from '../components/MediaDetails';
import { WriteEditReview } from '../components/WriteEditReview';
import { PleaseLogin } from '../components/PleaseLogin';
import { MediaDto } from '../types/types';
import { useAuth } from '../context/AuthContext';

export function MediaReviewPage() {
    const { mediaType, externalApiId } = useParams();
    const [searchParams, setSearchParams] = useSearchParams();
    const location = useLocation();

    const { isLoggedIn } = useAuth();
    const media = location.state?.media as MediaDto;

    const activeTab = searchParams.get('tab') || 'details';
    const handleTabChange = (_event: any, newValue: string) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            newParams.set('tab', newValue);
            return newParams;
        }, { state: location.state });
    };

    if (!media) {
        return <Navigate to="/search" replace />;
    }

    return <Box sx={{ mt: 2 }}>
        <Tabs value={activeTab} onChange={handleTabChange} centered sx={{ mb: 4 }}>
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