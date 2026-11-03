import { useEffect, useRef, useState } from "preact/hooks";
import { Alert, Box, Button, Chip, CircularProgress, Divider, List, ListItem, ListItemText, Stack, Typography } from "@mui/material";
import CameraAltIcon from "@mui/icons-material/CameraAlt";
import PhotoLibraryIcon from "@mui/icons-material/PhotoLibrary";
import ReplayIcon from "@mui/icons-material/Replay";
import { useAuth } from "../context/AuthContext";
import { PleaseLogin } from "../components/PleaseLogin";
import { PremiumUpsell } from "../components/PremiumUpsell";
import { MediaCard } from "../components/MediaCard";
import { CameraCapture } from "../components/CameraCapture";
import { recognitionService } from "../services/RecognitionService";
import { downscaleToJpeg } from "../utils/image";
import { allowanceText, emptyResultMessage, formatResetTime, itemKey, mergeItems, RECOGNITION_TIPS, tierLabel } from "../utils/recognition";
import { RecognitionAllowanceDto, RecognitionResponseDto, RecognizedMediaDto } from "../types/types";

const NOTICE_KEY = 'recognitionNoticeDismissed';

type Source = 'camera' | 'file';

type Step =
    | { kind: 'start' }
    | { kind: 'camera' }
    | { kind: 'preview', photo: Blob, url: string, source: Source }
    | { kind: 'progress', photo: Blob, url: string, source: Source }
    | { kind: 'result', response: RecognitionResponseDto };

function readNoticeDismissed(): boolean {
    try {
        return localStorage.getItem(NOTICE_KEY) === 'true';
    } catch {
        return false;
    }
}

// Camera recognition (Premium): take or pick a photo of something movie, series or music related,
// and get a list of related media, closest first, 10 at a time plus 5 per "Show more"
export function RecognitionPage() {
    const { user, isLoggedIn, isLoading } = useAuth();

    const [allowance, setAllowance] = useState<RecognitionAllowanceDto>();
    const [allowanceError, setAllowanceError] = useState('');
    const [step, setStep] = useState<Step>({ kind: 'start' });
    const [error, setError] = useState('');
    const [cameraMessage, setCameraMessage] = useState('');
    const [noticeDismissed, setNoticeDismissed] = useState(readNoticeDismissed);

    // The result list and its paging
    const [items, setItems] = useState<RecognizedMediaDto[]>([]);
    const [hasMore, setHasMore] = useState(false);
    const [nextOffset, setNextOffset] = useState(0);
    const [loadingMore, setLoadingMore] = useState(false);
    const [moreError, setMoreError] = useState('');

    const fileInputRef = useRef<HTMLInputElement>(null);
    const abortRef = useRef<AbortController | null>(null);

    const loadAllowance = async () => {
        setAllowanceError('');
        const result = await recognitionService.getAllowance();
        if (result.success) setAllowance(result.data);
        else setAllowanceError(result.message);
    };

    // A different (or no) user never sees the previous one's photo or results
    useEffect(() => {
        abortRef.current?.abort();
        setStep({ kind: 'start' });
        setItems([]);
        setError('');
        setAllowance(undefined);
        if (isLoggedIn) loadAllowance();
    }, [user?.id]);

    // The photo's preview URL lives as long as the photo is on screen
    const photoUrl = step.kind === 'preview' || step.kind === 'progress' ? step.url : undefined;
    useEffect(() => () => { if (photoUrl) URL.revokeObjectURL(photoUrl); }, [photoUrl]);

    // Leaving the page stops waiting for a running recognition
    useEffect(() => () => abortRef.current?.abort(), []);

    const showPreview = (photo: Blob, source: Source) => {
        setError('');
        setStep({ kind: 'preview', photo, url: URL.createObjectURL(photo), source });
    };

    const handleFileChosen = async (event: Event) => {
        const input = event.currentTarget as HTMLInputElement;
        const file = input.files?.[0];
        input.value = ''; // choosing the same file again still fires a change
        if (!file) return;

        try {
            showPreview(await downscaleToJpeg(file), 'file');
        } catch {
            setError("This file couldn't be read as an image. Choose a JPEG or PNG photo.");
            setStep({ kind: 'start' });
        }
    };

    const handleRetake = () => {
        setError('');
        setStep(step.kind === 'preview' && step.source === 'camera' && !cameraMessage ? { kind: 'camera' } : { kind: 'start' });
    };

    const handleUsePhoto = async () => {
        if (step.kind !== 'preview') return;
        const current = step;
        const controller = new AbortController();
        abortRef.current = controller;
        setError('');
        setStep({ ...current, kind: 'progress' });

        const result = await recognitionService.recognize(current.photo, controller.signal);
        if (controller.signal.aborted)
            return;
        abortRef.current = null;

        if (result.success) {
            const response = result.data;
            setAllowance(response.allowance);
            setItems(response.items);
            setHasMore(response.hasMore);
            setNextOffset(response.nextOffset);
            setMoreError('');
            setStep({ kind: 'result', response });
            return;
        }

        if (result.status === 402) {
            // The upsell dialog opens by itself; the page shows the Free state
            setAllowance(a => a && { ...a, premium: false });
            setStep({ kind: 'start' });
            return;
        }

        if (result.code === 'daily_limit')
            setAllowance(a => a && { ...a, remaining: 0, resetsAt: result.resetsAt ?? a.resetsAt });

        setError(result.message);
        setStep(current);
    };

    const handleCancel = () => {
        abortRef.current?.abort();
        abortRef.current = null;
        if (step.kind === 'progress')
            setStep({ ...step, kind: 'preview' });
    };

    const handleShowMore = async () => {
        if (step.kind !== 'result' || !step.response.recognitionId) return;
        setLoadingMore(true);
        setMoreError('');

        const result = await recognitionService.getItems(step.response.recognitionId, nextOffset);
        setLoadingMore(false);

        if (result.success) {
            setItems(current => mergeItems(current, result.data.items));
            setHasMore(result.data.hasMore);
            setNextOffset(result.data.nextOffset);
            return;
        }

        // 402: the upsell opens by itself, the list stays
        if (result.status === 402) return;
        if (result.code === 'not_found') {
            setHasMore(false);
            setMoreError('These suggestions have expired. Take a new photo to see more.');
            return;
        }
        setMoreError(result.message);
    };

    const handleDismissNotice = () => {
        setNoticeDismissed(true);
        try {
            localStorage.setItem(NOTICE_KEY, 'true');
        } catch {
            // Only a convenience: the notice simply shows again next time
        }
    };

    /* Rendering */

    if (isLoading)
        return <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box>;

    if (!isLoggedIn)
        return <PleaseLogin message="Log in to use camera recognition." />;

    const header = <Box textAlign="center">
        <CameraAltIcon sx={{ fontSize: 48 }} color="primary" />
        <Typography variant="h4" component="h1" fontWeight="bold">Camera recognition</Typography>
        <Typography color="text.secondary" sx={{ mt: 1 }}>
            Take a photo of a figure, poster, album cover or other collectible, and find the movies, series and music it relates to.
        </Typography>
    </Box>;

    if (allowanceError)
        return <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto', py: 2 }}>
            {header}
            <Alert severity="error" action={<Button color="inherit" onClick={loadAllowance}>Retry</Button>}>{allowanceError}</Alert>
        </Stack>;

    if (!allowance)
        return <Box display="flex" justifyContent="center" py={6}><CircularProgress aria-label="Loading" /></Box>;

    if (!allowance.enabled)
        return <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto', py: 2 }}>
            {header}
            <Alert severity="info">Camera recognition is temporarily unavailable. Please check back later.</Alert>
        </Stack>;

    if (!allowance.premium)
        return <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto', py: 2 }}>
            {header}
            <PremiumUpsell title="Camera recognition is a Premium feature"
                message="Subscribe to Premium to recognise figures, posters, covers and collectibles with your camera." />
        </Stack>;

    const outOfRecognitions = allowance.remaining === 0;

    return <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto', py: 2 }}>
        {header}

        <Box display="flex" justifyContent="center">
            <Chip label={allowanceText(allowance.remaining, allowance.limit)} color={outOfRecognitions ? 'warning' : 'default'} />
        </Box>

        {!noticeDismissed && (step.kind === 'start' || step.kind === 'camera') && (
            <Alert severity="info" onClose={handleDismissNotice}>
                Your photo is sent to an external AI service (Google Gemini) for analysis. It is not stored, and it is only used for this recognition.
            </Alert>
        )}

        {/* Progress and errors are announced to screen readers */}
        <Box aria-live="polite">
            {error && <Alert severity="error">{error}</Alert>}
            {step.kind === 'progress' && <Typography textAlign="center">Recognising your photo...</Typography>}
        </Box>

        <input ref={fileInputRef} type="file" accept="image/*" hidden onChange={handleFileChosen} data-testid="photo-input" />

        {step.kind === 'start' && (
            outOfRecognitions
                ? <Alert severity="warning">
                    You have used all your recognitions for today. They reset at {formatResetTime(allowance.resetsAt)}.
                </Alert>
                : <Stack spacing={2} alignItems="center">
                    {cameraMessage && <Alert severity="info">{cameraMessage}</Alert>}
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                        {!cameraMessage && <Button variant="contained" size="large" startIcon={<CameraAltIcon />} onClick={() => setStep({ kind: 'camera' })}
                            sx={{ borderRadius: 5, textTransform: 'none' }}>
                            Take a photo
                        </Button>}
                        <Button variant={cameraMessage ? 'contained' : 'outlined'} size="large" startIcon={<PhotoLibraryIcon />}
                            onClick={() => fileInputRef.current?.click()} sx={{ borderRadius: 5, textTransform: 'none' }}>
                            Choose a photo
                        </Button>
                    </Stack>
                    <Tips />
                </Stack>
        )}

        {step.kind === 'camera' && <Stack spacing={2} alignItems="center">
            <CameraCapture
                onCapture={photo => showPreview(photo, 'camera')}
                onUnavailable={reason => {
                    setCameraMessage(reason);
                    setStep({ kind: 'start' });
                }}
            />
            <Button onClick={() => setStep({ kind: 'start' })}>Back</Button>
        </Stack>}

        {(step.kind === 'preview' || step.kind === 'progress') && <Stack spacing={2} alignItems="center">
            <Box component="img" src={step.url} alt="Your photo"
                sx={{ maxWidth: '100%', maxHeight: 480, borderRadius: 2, opacity: step.kind === 'progress' ? 0.5 : 1 }} />
            {step.kind === 'preview'
                ? <Stack direction="row" spacing={2}>
                    <Button variant="contained" size="large" onClick={handleUsePhoto} disabled={outOfRecognitions}
                        sx={{ borderRadius: 5, textTransform: 'none' }}>
                        Use photo
                    </Button>
                    <Button variant="outlined" size="large" onClick={handleRetake} sx={{ borderRadius: 5, textTransform: 'none' }}>
                        Retake
                    </Button>
                </Stack>
                : <Stack direction="row" spacing={2} alignItems="center">
                    <CircularProgress size={28} aria-label="Recognising" />
                    <Button onClick={handleCancel}>Cancel</Button>
                </Stack>}
        </Stack>}

        {step.kind === 'result' && <Stack spacing={2}>
            <Alert severity={step.response.outcome === 'recognised' ? 'success' : 'info'} icon={false}>
                <Typography fontWeight="bold">{step.response.summary}</Typography>
            </Alert>

            {step.response.incomplete && (
                <Alert severity="warning">Some movie, series or music sources didn't answer, so the list may be incomplete.</Alert>
            )}

            {items.length === 0
                ? <Stack spacing={1}>
                    <Typography>{emptyResultMessage(step.response.outcome)}</Typography>
                    <Tips />
                </Stack>
                : <Box>
                    {items.map((item, index) => <Box key={itemKey(item)}>
                        {/* A divider where the relation gets looser */}
                        {(index === 0 || items[index - 1].tier !== item.tier) && (
                            <Typography variant="overline" color="text.secondary" component="h2">{tierLabel(item.tier)}</Typography>
                        )}
                        <MediaCard media={item.media} badge={tierLabel(item.tier)} />
                    </Box>)}
                </Box>}

            {moreError && <Alert severity="error" action={hasMore && <Button color="inherit" onClick={handleShowMore}>Retry</Button>}>{moreError}</Alert>}

            {items.length > 0 && (hasMore
                ? !moreError && <Box display="flex" justifyContent="center">
                    <Button variant="outlined" onClick={handleShowMore} disabled={loadingMore}
                        startIcon={loadingMore ? <CircularProgress size={18} /> : undefined}>
                        Show more
                    </Button>
                </Box>
                : !moreError && <Typography textAlign="center" color="text.secondary">That's everything we found.</Typography>)}

            <Divider />
            <Box display="flex" justifyContent="center">
                <Button variant="contained" startIcon={<ReplayIcon />} onClick={() => setStep({ kind: 'start' })}
                    sx={{ borderRadius: 5, textTransform: 'none' }}>
                    Try another photo
                </Button>
            </Box>
        </Stack>}
    </Stack>;
}

function Tips() {
    return <Box>
        <Typography variant="subtitle2">Tips for a good result</Typography>
        <List dense disablePadding>
            {RECOGNITION_TIPS.map(tip => <ListItem key={tip} disableGutters sx={{ py: 0 }}>
                <ListItemText primary={`• ${tip}`} />
            </ListItem>)}
        </List>
    </Box>;
}
