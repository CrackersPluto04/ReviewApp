import { useEffect, useRef, useState } from "preact/hooks";
import { Alert, Box, Button, CircularProgress, Stack } from "@mui/material";
import PhotoCameraIcon from "@mui/icons-material/PhotoCamera";
import CameraswitchIcon from "@mui/icons-material/Cameraswitch";
import { captureFrame } from "../utils/image";

type CameraCaptureProps = {
    onCapture: (photo: Blob) => void;
    // The camera can't be used here (no camera, denied, insecure page): the page offers the file picker instead
    onUnavailable: (reason: string) => void;
};

type Facing = 'environment' | 'user';

export function cameraErrorMessage(error: unknown): string {
    const name = error instanceof DOMException ? error.name : '';
    switch (name) {
        case 'NotAllowedError':
        case 'SecurityError':
            return 'Camera access was denied. Allow it in your browser settings, or choose a photo instead.';
        case 'NotFoundError':
        case 'OverconstrainedError':
            return 'No camera was found. Choose a photo instead.';
        case 'NotReadableError':
        case 'AbortError':
            return 'The camera is in use by another app. Close it and try again, or choose a photo instead.';
        default:
            return 'The camera could not be started. Choose a photo instead.';
    }
}

// The live webcam picture with a "Take photo" button. The camera is only on while this is shown:
// it is turned off when the photo is taken, when the tab is hidden and when the component goes away.
export function CameraCapture({ onCapture, onUnavailable }: CameraCaptureProps) {
    const videoRef = useRef<HTMLVideoElement>(null);
    const streamRef = useRef<MediaStream | null>(null);
    const [facing, setFacing] = useState<Facing>('environment');
    const [ready, setReady] = useState(false);
    const [paused, setPaused] = useState(false);
    const [canSwitch, setCanSwitch] = useState(false);
    const [capturing, setCapturing] = useState(false);
    const [error, setError] = useState('');

    const stop = () => {
        streamRef.current?.getTracks().forEach(track => track.stop());
        streamRef.current = null;
        setReady(false);
    };

    useEffect(() => {
        if (paused) return;

        if (!globalThis.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
            onUnavailable('The camera is not available in this browser. Choose a photo instead.');
            return;
        }

        let cancelled = false;
        const start = async () => {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: facing }, width: { ideal: 1920 }, height: { ideal: 1080 } },
                    audio: false
                });
                if (cancelled) {
                    stream.getTracks().forEach(track => track.stop());
                    return;
                }
                streamRef.current = stream;
                if (videoRef.current) {
                    videoRef.current.srcObject = stream;
                    await videoRef.current.play().catch(() => undefined);
                }
                setReady(true);

                // Labels and the list of cameras are only known once access was granted
                const devices = await navigator.mediaDevices.enumerateDevices?.().catch(() => []) ?? [];
                if (!cancelled)
                    setCanSwitch(devices.filter(d => d.kind === 'videoinput').length > 1);
            } catch (e) {
                if (!cancelled)
                    onUnavailable(cameraErrorMessage(e));
            }
        };
        start();

        return () => {
            cancelled = true;
            stop();
        };
    }, [facing, paused]);

    // A hidden tab never keeps the camera on
    useEffect(() => {
        const onVisibilityChange = () => {
            if (document.visibilityState === 'hidden')
                setPaused(true);
        };
        document.addEventListener('visibilitychange', onVisibilityChange);
        return () => document.removeEventListener('visibilitychange', onVisibilityChange);
    }, []);

    const handleTakePhoto = async () => {
        if (!videoRef.current) return;
        setCapturing(true);
        setError('');
        try {
            const photo = await captureFrame(videoRef.current);
            stop();
            onCapture(photo);
        } catch (e) {
            setError(e instanceof Error ? e.message : 'The photo could not be taken.');
            setCapturing(false);
        }
    };

    if (paused)
        return <Stack spacing={2} alignItems="center" sx={{ py: 4 }}>
            <Alert severity="info">The camera was turned off while the page was in the background.</Alert>
            <Button variant="contained" startIcon={<PhotoCameraIcon />} onClick={() => setPaused(false)}>Turn the camera back on</Button>
        </Stack>;

    return <Stack spacing={2} alignItems="center">
        <Box sx={{ position: 'relative', width: '100%', maxWidth: 720, aspectRatio: '4 / 3', bgcolor: 'black', borderRadius: 2, overflow: 'hidden' }}>
            <video ref={videoRef} autoPlay playsInline muted aria-label="Camera preview"
                style={{ width: '100%', height: '100%', objectFit: 'contain' }} />
            {!ready && <Box sx={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <CircularProgress aria-label="Starting the camera" />
            </Box>}
        </Box>

        {error && <Alert severity="error" role="alert">{error}</Alert>}

        <Stack direction="row" spacing={2}>
            <Button variant="contained" size="large" startIcon={<PhotoCameraIcon />} onClick={handleTakePhoto} disabled={!ready || capturing}
                sx={{ borderRadius: 5, textTransform: 'none' }}>
                Take photo
            </Button>
            {canSwitch && <Button variant="outlined" startIcon={<CameraswitchIcon />} disabled={capturing}
                onClick={() => setFacing(f => f === 'environment' ? 'user' : 'environment')} sx={{ borderRadius: 5, textTransform: 'none' }}>
                Switch camera
            </Button>}
        </Stack>
    </Stack>;
}
