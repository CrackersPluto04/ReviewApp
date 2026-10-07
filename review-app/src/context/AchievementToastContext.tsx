import { useEffect, useState } from 'preact/hooks';
import { ReactNode } from 'preact/compat';
import { Alert, AlertTitle, Snackbar } from '@mui/material';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import { setUnlockHandler } from '../services/apiClient';
import { UnlockedAchievementDto } from '../types/types';
import { TIER_COLORS } from '../utils/achievements';

type AchievementToastProviderProps = {
    children: ReactNode;
};

// Shows a toast for every achievement tier unlocked by any API call (see apiClient.ts),
// so the components that trigger unlocks (reviews, replies, collections) don't need to know about it
export function AchievementToastProvider({ children }: AchievementToastProviderProps) {
    // One action can unlock several tiers, they are shown one after the other
    const [queue, setQueue] = useState<UnlockedAchievementDto[]>([]);
    const [open, setOpen] = useState(false);

    useEffect(() => {
        setUnlockHandler(unlocked => setQueue(prev => [...prev, ...unlocked]));
        return () => setUnlockHandler(null);
    }, []);

    const current = queue[0];

    // Open the next toast once the previous one has closed
    useEffect(() => {
        if (current && !open)
            setOpen(true);
    }, [current, open]);

    const handleClose = (_event?: unknown, reason?: string) => {
        if (reason === 'clickaway') return;
        setOpen(false);
    };

    return <>
        {children}

        <Snackbar
            key={current ? `${current.groupCode}-${current.tier}` : undefined}
            open={open && !!current}
            autoHideDuration={5000}
            onClose={handleClose}
            anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
            sx={{ mt: 7 }}
            slotProps={{ transition: { onExited: () => setQueue(prev => prev.slice(1)) } }}
        >
            {current && (
                <Alert
                    onClose={handleClose}
                    variant="filled"
                    icon={<EmojiEventsIcon fontSize="large" sx={{ color: TIER_COLORS[current.tier] }} />}
                    // Dark in both themes so the tier colour of the trophy stands out
                    sx={{ width: '100%', alignItems: 'center', bgcolor: 'grey.900', color: 'common.white', boxShadow: 6 }}
                >
                    <AlertTitle sx={{ mb: 0 }}>Achievement unlocked!</AlertTitle>
                    {current.title} ({current.tier})
                </Alert>
            )}
        </Snackbar>
    </>
}
