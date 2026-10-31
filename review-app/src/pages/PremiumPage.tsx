import { useEffect, useState } from "preact/hooks";
import {
    Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, Link, List, ListItem, ListItemIcon, ListItemText, Stack, Typography
} from "@mui/material";
import WorkspacePremiumIcon from "@mui/icons-material/WorkspacePremium";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import ScheduleIcon from "@mui/icons-material/Schedule";
import { useLocation, useNavigate } from "react-router-dom";
import { PlanDto } from "../types/types";
import { subscriptionService } from "../services/SubscriptionService";
import { useAuth } from "../context/AuthContext";
import { formatPrice, PREMIUM_COLOR } from "../utils/subscription";
import { SubscriptionPanel } from "../components/SubscriptionPanel";

const FREE_FEATURES = [
    'Reviews, replies and scores',
    'Collections and favourites',
    'Followers and achievements',
    'Everything the app offers today stays free'
];

// Plans page: Free vs Premium, subscribing, and the current subscription for subscribers. Visible without logging in.
export function PremiumPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { user, isLoggedIn } = useAuth();

    const [plan, setPlan] = useState<PlanDto>();
    const [loadError, setLoadError] = useState('');
    const [checkoutError, setCheckoutError] = useState('');
    const [redirecting, setRedirecting] = useState(false);
    const [previewResult, setPreviewResult] = useState<{ success: boolean, message: string }>();

    useEffect(() => {
        const load = async () => {
            const result = await subscriptionService.getPlan();
            if (result.success) setPlan(result.data);
            else setLoadError(result.message ?? 'Something went wrong.');
        };
        load();
    }, []);

    const handleSubscribe = async () => {
        if (!isLoggedIn) {
            navigate('/login', { state: { returnTo: location } });
            return;
        }

        setRedirecting(true);
        setCheckoutError('');
        const result = await subscriptionService.startCheckout();
        if (result.success && result.data?.url) {
            // Payment happens on the provider's page, which sends the user back to /subscription/return
            globalThis.location.href = result.data.url;
            return;
        }
        setCheckoutError(result.message ?? 'Something went wrong.');
        setRedirecting(false);
    };

    // A Free user gets a 402 here, which opens the upsell dialog by itself
    const handleTryPreview = async () => {
        const result = await subscriptionService.tryEntitlementPreview();
        setPreviewResult(result.success || result.status !== 402
            ? { success: result.success, message: result.message ?? 'Something went wrong.' }
            : undefined);
    };

    const isPremium = user?.isPremium ?? false;

    return <Stack spacing={4} sx={{ maxWidth: 960, mx: 'auto', py: 2 }}>
        <Box textAlign="center">
            <WorkspacePremiumIcon sx={{ fontSize: 56, color: PREMIUM_COLOR }} />
            <Typography variant="h4" component="h1" fontWeight="bold">THE Review App Premium</Typography>
            <Typography color="text.secondary" sx={{ mt: 1 }}>
                Support the app and unlock exclusive features. Cancel any time.
            </Typography>
        </Box>

        {loadError && <Alert severity="error">{loadError}</Alert>}
        {!plan && !loadError && <Box display="flex" justifyContent="center"><CircularProgress /></Box>}

        {plan && <Stack direction={{ xs: 'column', md: 'row' }} spacing={3} alignItems="stretch">
            {/* Free */}
            <Card variant="outlined" sx={{ flex: 1, borderRadius: 2 }}>
                <CardContent>
                    <Typography variant="h5" fontWeight="bold">Free</Typography>
                    <Typography variant="h6" color="text.secondary" sx={{ mb: 1 }}>Forever</Typography>
                    <List dense>
                        {FREE_FEATURES.map(feature => (
                            <ListItem key={feature} disableGutters>
                                <ListItemIcon sx={{ minWidth: 36 }}><CheckCircleOutlineIcon color="success" /></ListItemIcon>
                                <ListItemText primary={feature} />
                            </ListItem>
                        ))}
                    </List>
                    {isLoggedIn && !isPremium && <Chip label="Your current plan" sx={{ mt: 1 }} />}
                </CardContent>
            </Card>

            {/* Premium */}
            <Card variant="outlined" sx={{ flex: 1, borderRadius: 2, borderColor: PREMIUM_COLOR, borderWidth: 2 }}>
                <CardContent>
                    <Stack direction="row" spacing={1} alignItems="center">
                        <Typography variant="h5" fontWeight="bold">Premium</Typography>
                        <WorkspacePremiumIcon sx={{ color: PREMIUM_COLOR }} />
                    </Stack>
                    <Typography variant="h6" sx={{ mb: 1 }}>{formatPrice(plan)}</Typography>
                    <List dense>
                        {plan.features.map(feature => (
                            <ListItem key={feature.key} disableGutters>
                                <ListItemIcon sx={{ minWidth: 36 }}>
                                    {feature.comingSoon
                                        ? <ScheduleIcon color="disabled" />
                                        : <CheckCircleOutlineIcon sx={{ color: PREMIUM_COLOR }} />}
                                </ListItemIcon>
                                <ListItemText
                                    primary={<>
                                        {feature.title}
                                        {feature.comingSoon && <Chip size="small" label="Coming soon" sx={{ ml: 1 }} />}
                                    </>}
                                    secondary={feature.description}
                                />
                            </ListItem>
                        ))}
                    </List>

                    {checkoutError && <Alert severity="error" sx={{ mt: 1 }}>{checkoutError}</Alert>}

                    {isPremium
                        ? <Chip label="Your current plan" sx={{ mt: 1, bgcolor: PREMIUM_COLOR, color: '#000' }} />
                        : <Button variant="contained" size="large" fullWidth sx={{ mt: 1, borderRadius: 5, textTransform: 'none' }}
                            onClick={handleSubscribe} disabled={redirecting}
                        >
                            {isLoggedIn ? `Subscribe for ${formatPrice(plan)}` : 'Log in to subscribe'}
                        </Button>}

                    <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1.5 }}>
                        Premium renews automatically every {plan.interval} until you cancel. You can cancel any time and keep
                        Premium until the end of the period you paid for. Payments are handled securely by our payment provider.
                        By subscribing you agree to the <Link href="#terms">Terms of Service</Link>.
                    </Typography>
                </CardContent>
            </Card>
        </Stack>}

        {isLoggedIn && isPremium && <>
            <Divider />
            <Box>
                <Typography variant="h5" component="h2" fontWeight="bold" sx={{ mb: 2 }}>Your subscription</Typography>
                <SubscriptionPanel />
            </Box>
        </>}

        {isLoggedIn && <>
            <Divider />
            <Box>
                <Typography variant="h5" component="h2" fontWeight="bold">Early access</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 1.5 }}>
                    Premium members can try new features before anyone else.
                </Typography>
                <Button variant="outlined" onClick={handleTryPreview}>Open the early access preview</Button>
                {previewResult && (
                    <Alert severity={previewResult.success ? 'success' : 'error'} sx={{ mt: 1.5 }} onClose={() => setPreviewResult(undefined)}>
                        {previewResult.message}
                    </Alert>
                )}
            </Box>
        </>}

        <Box id="terms">
            <Typography variant="subtitle2" fontWeight="bold">Terms of Service</Typography>
            <Typography variant="caption" color="text.secondary">
                The full terms are not published yet. Premium is currently in test mode: no real payments are taken.
            </Typography>
        </Box>
    </Stack>
}
