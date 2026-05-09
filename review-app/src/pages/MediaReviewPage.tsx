import { useLocation, Navigate, useParams, useSearchParams } from 'react-router-dom';
import { Box, Tabs, Tab, CircularProgress } from '@mui/material';
import { MediaDetails } from '../components/MediaDetails';
import { WriteEditReview } from '../components/WriteEditReview';
import { PleaseLogin } from '../components/PleaseLogin';
import { MediaDto } from '../types/types';
import { useAuth } from '../context/AuthContext';
import { mediaService } from '../services/MediaService';
import { useState, useEffect } from 'preact/hooks';

export function MediaReviewPage() {
    const { isLoggedIn } = useAuth();
    const { mediaType, externalApiId } = useParams();
    const [searchParams, setSearchParams] = useSearchParams();
    const location = useLocation();
    const [media, setMedia] = useState<MediaDto>(location.state?.media);
    const [loading, setLoading] = useState(!location.state?.media);

    const activeTab = searchParams.get('tab') || 'details';
    const handleTabChange = (_event: any, newValue: string) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            newParams.set('tab', newValue);
            return newParams;
        }, { state: location.state });
    };

    useEffect(() => {
        const fetchMediaDetails = async () => {
            if (!media && mediaType && externalApiId) {
                const result = await mediaService.getMediaDetails(mediaType, externalApiId);

                if (result.success) {
                    setMedia(result.data);
                }
            }

            setLoading(false);
        };

        fetchMediaDetails();
    }, [mediaType, externalApiId]);

    if (loading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
            <CircularProgress />
        </Box>
    }

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