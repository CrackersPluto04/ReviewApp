import { Chip, Tooltip } from "@mui/material";
import WorkspacePremiumIcon from "@mui/icons-material/WorkspacePremium";
import { PREMIUM_COLOR } from "../utils/subscription";

type PremiumBadgeProps = {
    size?: 'small' | 'medium';
};

// Public badge of subscribed users (profile header)
export function PremiumBadge({ size = 'small' }: PremiumBadgeProps) {
    return <Tooltip title="Premium member">
        <Chip
            size={size}
            icon={<WorkspacePremiumIcon />}
            label="Premium"
            aria-label="Premium member"
            variant="outlined"
            sx={{
                fontWeight: 'bold',
                color: PREMIUM_COLOR,
                borderColor: PREMIUM_COLOR,
                '& .MuiChip-icon': { color: PREMIUM_COLOR }
            }}
        />
    </Tooltip>
}
