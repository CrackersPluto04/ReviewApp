import { useEffect, useMemo, useState } from "preact/hooks";
import { useParams, useSearchParams } from "react-router-dom";
import { Box, Button, CircularProgress, Typography } from "@mui/material";
import { achievementService } from "../services/AchievementService";
import { AchievementDto } from "../types/types";
import { AchievementSort, AchievementStatus, CATEGORY_OPTIONS, SORT_OPTIONS, STATUS_OPTIONS, filterAndSortAchievements, unlockedTierCount } from "../utils/achievements";
import { AchievementCard } from "./AchievementCard";
import { AchievementFilterBar } from "./AchievementFilterBar";

export function AchievementsTab() {
    const { username } = useParams();
    const [searchParams, setSearchParams] = useSearchParams();

    // --- States ---
    const [achievements, setAchievements] = useState<AchievementDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState('');

    // --- Active States (From URL, unknown values are ignored) ---
    const statuses = searchParams.getAll('status')
        .filter((s): s is AchievementStatus => STATUS_OPTIONS.some(o => o.value === s));
    const categories = searchParams.getAll('category')
        .filter(c => CATEGORY_OPTIONS.includes(c));
    const sortParam = searchParams.get('sort');
    const sort: AchievementSort = SORT_OPTIONS.some(o => o.value === sortParam) ? sortParam as AchievementSort : 'default';

    useEffect(() => {
        const fetchAchievements = async () => {
            if (!username) return;
            setLoading(true);
            setErrorMessage('');

            const result = await achievementService.getUserAchievements(username);
            if (result.success)
                setAchievements(result.data ?? []);
            else {
                setAchievements([]);
                setErrorMessage(result.message);
            }

            setLoading(false);
        };

        fetchAchievements();
    }, [username]);

    // Filtering is client-side, the whole list is small
    const filterKey = searchParams.toString();
    const visibleAchievements = useMemo(
        () => filterAndSortAchievements(achievements, statuses, categories, sort),
        [achievements, filterKey]
    );

    const earnedTiers = achievements.reduce((sum, a) => sum + unlockedTierCount(a), 0);
    const totalTiers = achievements.reduce((sum, a) => sum + a.tiers.length, 0);

    /* Handlers */
    // Toggles one value of a repeatable URL param, replace so every chip click isn't a history entry
    const toggleParam = (key: string, value: string) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            const values = newParams.getAll(key);

            newParams.delete(key);
            const nextValues = values.includes(value) ? values.filter(v => v !== value) : [...values, value];
            nextValues.forEach(v => newParams.append(key, v));

            return newParams;
        }, { replace: true });
    };

    const handleSortChange = (newSort: AchievementSort) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev);
            if (newSort === 'default') newParams.delete('sort');
            else newParams.set('sort', newSort);
            return newParams;
        }, { replace: true });
    };

    const handleReset = () => {
        setSearchParams({}, { replace: true });
    };

    return <Box>
        <Box sx={{ display: 'flex', alignItems: 'baseline', flexWrap: 'wrap', gap: 2, mb: 3 }}>
            <Typography variant="h5" fontWeight="bold">
                Achievements
            </Typography>

            {!loading && totalTiers > 0 && (
                <Typography color="text.secondary">
                    {earnedTiers} of {totalTiers} tiers earned
                </Typography>
            )}
        </Box>

        <AchievementFilterBar
            statuses={statuses}
            categories={categories}
            sort={sort}
            onToggleStatus={s => toggleParam('status', s)}
            onToggleCategory={c => toggleParam('category', c)}
            onSortChange={handleSortChange}
            onReset={handleReset}
        />

        {loading ? (
            <Box display="flex" justifyContent="center" p={4}><CircularProgress /></Box>
        ) : visibleAchievements.length === 0 ? (
            <Box sx={{ p: 5, textAlign: 'center', border: '1px dashed grey', borderRadius: 2, color: 'text.secondary' }}>
                <Typography color={errorMessage ? "error" : "text.secondary"}>
                    {errorMessage || (achievements.length === 0 ? 'No achievements yet.' : 'No achievements match these filters.')}
                </Typography>

                {!errorMessage && achievements.length > 0 && (
                    <Button sx={{ mt: 2 }} onClick={handleReset}>Reset filters</Button>
                )}
            </Box>
        ) : (
            // Keyed by the filters so cards remount unflipped when the selection changes
            <Box
                key={filterKey}
                sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 2 }}
            >
                {visibleAchievements.map(a => (
                    <AchievementCard key={a.groupCode} achievement={a} />
                ))}
            </Box>
        )}
    </Box>
}
