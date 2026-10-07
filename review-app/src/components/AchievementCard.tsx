import { Box, Chip, LinearProgress, Paper, Typography } from "@mui/material";
import EmojiEventsIcon from "@mui/icons-material/EmojiEvents";
import { useState } from "preact/hooks";
import { AchievementDto } from "../types/types";
import { iconUrl, isCompleted, nextTier, unlockedTierCount } from "../utils/achievements";
import { TierMedals } from "./TierMedals";

type AchievementCardProps = {
    achievement: AchievementDto;
};

const CARD_HEIGHT = 260;

// Shared by both faces: stacked on top of each other, the one facing away is hidden
const faceSx = {
    position: 'absolute',
    inset: 0,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    p: 2,
    borderRadius: 2,
    backfaceVisibility: 'hidden',
    WebkitBackfaceVisibility: 'hidden',
    overflow: 'hidden'
} as const;

const clampSx = (lines: number) => ({
    display: '-webkit-box',
    WebkitLineClamp: lines,
    WebkitBoxOrient: 'vertical',
    overflow: 'hidden',
    overflowWrap: 'anywhere'
}) as const;

// Steam-like achievement card: the front shows the icon, tier medals and title,
// clicking (or Enter / Space) flips it to the description and progress
export function AchievementCard({ achievement }: AchievementCardProps) {
    const [flipped, setFlipped] = useState(false);
    const [iconFailed, setIconFailed] = useState(false);

    const unlocked = unlockedTierCount(achievement);
    const tierCount = achievement.tiers.length;
    const isLocked = unlocked === 0;
    const completed = isCompleted(achievement);
    const next = nextTier(achievement);
    // The goal being worked on, or the last one once everything is earned
    const shownTier = next ?? achievement.tiers[tierCount - 1];

    const toggle = () => setFlipped(prev => !prev);

    const handleKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault(); // Space would scroll the page
            toggle();
        }
    };

    return <Box
        role="button"
        tabIndex={0}
        aria-pressed={flipped}
        aria-label={`${achievement.title}, ${unlocked} of ${tierCount} tiers unlocked. ${flipped ? 'Hide details' : 'Show details'}`}
        onClick={toggle}
        onKeyDown={handleKeyDown}
        sx={{
            height: CARD_HEIGHT,
            perspective: '1000px',
            cursor: 'pointer',
            borderRadius: 2,
            outline: 'none',
            '&:focus-visible': { outline: '3px solid', outlineColor: 'primary.main', outlineOffset: 3 }
        }}
    >
        <Box sx={{
            position: 'relative',
            height: '100%',
            transformStyle: 'preserve-3d',
            transition: 'transform 0.5s ease',
            transform: flipped ? 'rotateY(180deg)' : 'none',
            // Faces swap instantly instead of rotating
            '@media (prefers-reduced-motion: reduce)': { transition: 'none' }
        }}>
            {/* FRONT */}
            <Paper variant="outlined" aria-hidden={flipped} sx={faceSx}>
                {/* Greyscale is applied inside the face, a filter on the face itself could break the 3D flip */}
                <Box sx={{
                    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 1.5,
                    width: '100%', height: '100%',
                    filter: isLocked ? 'grayscale(1)' : 'none',
                    opacity: isLocked ? 0.55 : 1
                }}>
                    <Box sx={{ width: 96, height: 96, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        {iconFailed ? (
                            <EmojiEventsIcon sx={{ fontSize: 80, color: 'text.secondary' }} />
                        ) : (
                            <Box
                                component="img"
                                src={iconUrl(achievement.iconKey)}
                                alt=""
                                onError={() => setIconFailed(true)}
                                sx={{ width: '100%', height: '100%', objectFit: 'contain' }}
                            />
                        )}
                    </Box>

                    <TierMedals tiers={achievement.tiers} />

                    <Typography variant="subtitle1" fontWeight="bold" textAlign="center" sx={clampSx(2)}>
                        {achievement.title}
                    </Typography>
                </Box>
            </Paper>

            {/* BACK */}
            <Paper variant="outlined" aria-hidden={!flipped} sx={{ ...faceSx, transform: 'rotateY(180deg)', justifyContent: 'space-between', textAlign: 'center' }}>
                <Box sx={{ width: '100%' }}>
                    <Typography variant="subtitle1" fontWeight="bold" sx={clampSx(1)}>
                        {achievement.title}
                    </Typography>

                    <Typography variant="body2" color="text.secondary" sx={{ mt: 1, ...clampSx(4) }}>
                        {shownTier?.description}
                    </Typography>
                </Box>

                <Box sx={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 1 }}>
                    <Typography variant="body2">
                        Unlock: <strong>{unlocked}/{tierCount}</strong>
                    </Typography>

                    {completed ? (
                        <Chip icon={<EmojiEventsIcon />} label="Completed" color="success" size="small" sx={{ alignSelf: 'center' }} />
                    ) : next && (
                        <>
                            <Typography variant="body2">
                                Next tier ({next.tier}): <strong>{Math.min(achievement.currentProgress, next.targetValue)}/{next.targetValue}</strong>
                            </Typography>
                            <LinearProgress
                                variant="determinate"
                                value={Math.min(achievement.currentProgress / next.targetValue, 1) * 100}
                                sx={{ height: 6, borderRadius: 3 }}
                            />
                        </>
                    )}
                </Box>
            </Paper>
        </Box>
    </Box>
}
