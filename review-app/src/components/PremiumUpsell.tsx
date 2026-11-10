import { Box, Button, Paper, Typography } from "@mui/material";
import WorkspacePremiumIcon from "@mui/icons-material/WorkspacePremium";
import { useNavigate } from "react-router-dom";
import { PREMIUM_COLOR } from "../utils/subscription";

type PremiumUpsellProps = {
    /** The headline; defaults to "This is a Premium feature". */
    title?: string;
    /** The explanation under it; defaults to a generic subscribe text. */
    message?: string;
};

/**
 * Inline prompt for a locked Premium feature, linking to the plans page. Show it in place of the feature rather
 * than hiding the feature, so users can discover what Premium offers. The dialog variant is
 * `PremiumUpsellProvider`, which opens by itself on any 402 from the API.
 */
export function PremiumUpsell({
    title = 'This is a Premium feature',
    message = 'Subscribe to Premium to unlock it, along with every other Premium feature.'
}: PremiumUpsellProps) {
    const navigate = useNavigate();

    return <Paper variant="outlined" sx={{ p: 2, borderRadius: 2, borderColor: PREMIUM_COLOR, display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
        <WorkspacePremiumIcon sx={{ color: PREMIUM_COLOR, fontSize: 36 }} />
        <Box sx={{ flex: 1, minWidth: 200 }}>
            <Typography variant="subtitle1" fontWeight="bold">{title}</Typography>
            <Typography variant="body2" color="text.secondary">{message}</Typography>
        </Box>
        <Button variant="contained" onClick={() => navigate('/premium')} sx={{ textTransform: 'none', borderRadius: 5 }}>
            See Premium
        </Button>
    </Paper>
}
