import { createContext } from 'preact';
import { useContext, useEffect, useRef, useState } from 'preact/hooks';
import { ReactNode } from 'preact/compat';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import WorkspacePremiumIcon from '@mui/icons-material/WorkspacePremium';
import { useNavigate } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { setPremiumRequiredHandler } from '../services/apiClient';
import { PREMIUM_COLOR } from '../utils/subscription';

interface PremiumUpsellContextType {
    // Opens the "This is a Premium feature" dialog (also opened automatically on any 402 from the API)
    showUpsell: () => void;
}

type PremiumUpsellProviderProps = {
    children: ReactNode;
};

const PremiumUpsellContext = createContext<PremiumUpsellContextType | undefined>(undefined);

// Inside the router: the dialog links to the plans page
export function PremiumUpsellProvider({ children }: PremiumUpsellProviderProps) {
    const { refreshUser } = useAuth();
    const navigate = useNavigate();
    const [open, setOpen] = useState(false);

    const refreshUserRef = useRef(refreshUser);
    refreshUserRef.current = refreshUser;

    // A 402 can also mean the subscription just ended: reload the user so the UI stops offering Premium features
    useEffect(() => {
        setPremiumRequiredHandler(() => {
            setOpen(true);
            void refreshUserRef.current();
        });
        return () => setPremiumRequiredHandler(null);
    }, []);

    const handleSeePremium = () => {
        setOpen(false);
        navigate('/premium');
    };

    return <PremiumUpsellContext.Provider value={{ showUpsell: () => setOpen(true) }}>
        {children}

        <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="xs" aria-labelledby="premium-upsell-title">
            <DialogTitle id="premium-upsell-title">
                <Stack direction="row" spacing={1} alignItems="center">
                    <WorkspacePremiumIcon sx={{ color: PREMIUM_COLOR }} />
                    <span>This is a Premium feature</span>
                </Stack>
            </DialogTitle>
            <DialogContent dividers>
                <Typography variant="body2" color="text.secondary">
                    Subscribe to Premium to unlock it, along with every other Premium feature. You can cancel any time.
                </Typography>
            </DialogContent>
            <DialogActions>
                <Button color="inherit" onClick={() => setOpen(false)}>Not now</Button>
                <Button variant="contained" onClick={handleSeePremium}>See Premium</Button>
            </DialogActions>
        </Dialog>
    </PremiumUpsellContext.Provider>
}

export function usePremiumUpsell() {
    const context = useContext(PremiumUpsellContext);

    if (context === undefined) {
        throw new Error('usePremiumUpsell must be used within a PremiumUpsellProvider');
    }

    return context;
}
