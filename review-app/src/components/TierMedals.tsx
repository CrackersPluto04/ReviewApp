import { Box, Tooltip } from "@mui/material";
import MilitaryTechIcon from "@mui/icons-material/MilitaryTech";
import { AchievementTierDto } from "../types/types";
import { TIER_COLORS } from "../utils/achievements";

type TierMedalsProps = {
    tiers: AchievementTierDto[];
};

/** One medal per tier the achievement has, in the tier's colour once earned and greyed out before. */
export function TierMedals({ tiers }: TierMedalsProps) {
    return <Box sx={{ display: 'flex', justifyContent: 'center', gap: 0.5 }}>
        {tiers.map(t => (
            <Tooltip key={t.tier} title={`${t.tier}: ${t.isUnlocked ? 'earned' : 'not earned yet'}`}>
                <MilitaryTechIcon
                    aria-label={`${t.tier} ${t.isUnlocked ? 'earned' : 'not earned'}`}
                    sx={{
                        fontSize: 30,
                        color: t.isUnlocked ? TIER_COLORS[t.tier] : 'action.disabled',
                        // A slight shadow lifts earned medals off the background, so they don't blend into the greyed-out ones
                        filter: t.isUnlocked ? 'drop-shadow(0 1px 1px rgba(0, 0, 0, 0.4))' : 'none'
                    }}
                />
            </Tooltip>
        ))}
    </Box>
}
