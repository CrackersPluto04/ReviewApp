import { AchievementDto, AchievementTierDto, AchievementTierName } from "../types/types";

/** Medal colors per tier. */
export const TIER_COLORS: Record<AchievementTierName, string> = {
    Bronze: '#CD7F32',
    Silver: '#7A90AE',
    Gold: '#D4AF37'
};

/** A status filter of the achievements tab. The statuses overlap, see `matchesStatus`. */
export type AchievementStatus = 'unlocked' | 'locked' | 'in-progress';
/** The sort orders of the achievements tab. */
export type AchievementSort = 'default' | 'progress' | 'title';

export const STATUS_OPTIONS: { value: AchievementStatus; label: string }[] = [
    { value: 'unlocked', label: 'Unlocked' },
    { value: 'locked', label: 'Locked' },
    { value: 'in-progress', label: 'In progress' }
];

/** Category filter values. They are the names of the backend's `AchievementCategories` flags and must match them. */
export const CATEGORY_OPTIONS = ['Movie', 'Series', 'Music', 'Review', 'Reply', 'Collection'];

export const SORT_OPTIONS: { value: AchievementSort; label: string }[] = [
    { value: 'default', label: 'Unlocked first' },
    { value: 'progress', label: 'Most progress' },
    { value: 'title', label: 'Title (A-Z)' }
];

/**
 * The URL of an achievement's icon under `public/achievements/`.
 * @param iconKey The achievement's `iconKey`; a missing file is handled by the card (trophy fallback).
 */
export function iconUrl(iconKey: string) {
    return `/achievements/${iconKey}.svg`;
}

/**
 * How many of the achievement's tiers (bronze, silver, gold) are earned.
 * @param achievement The achievement with its tiers.
 */
export function unlockedTierCount(achievement: AchievementDto) {
    return achievement.tiers.filter(t => t.isUnlocked).length;
}

/**
 * The first tier still to earn.
 * @param achievement The achievement with its tiers, in tier order.
 * @returns The tier, or `undefined` when every tier is unlocked.
 */
export function nextTier(achievement: AchievementDto): AchievementTierDto | undefined {
    return achievement.tiers.find(t => !t.isUnlocked);
}

/**
 * Whether every tier is unlocked. An achievement without tiers is never completed.
 * @param achievement The achievement with its tiers.
 */
export function isCompleted(achievement: AchievementDto) {
    return achievement.tiers.length > 0 && nextTier(achievement) === undefined;
}

/**
 * Whether an achievement belongs to a status filter. The statuses overlap on purpose: an achievement with bronze
 * earned and silver underway is both unlocked and in progress.
 * @param achievement The achievement with its tiers and progress.
 * @param status `unlocked`: any tier earned; `locked`: none earned; `in-progress`: some progress and not completed.
 */
export function matchesStatus(achievement: AchievementDto, status: AchievementStatus) {
    const unlocked = unlockedTierCount(achievement);

    switch (status) {
        case 'unlocked': return unlocked > 0;
        case 'locked': return unlocked === 0;
        case 'in-progress': return achievement.currentProgress > 0 && !isCompleted(achievement);
    }
}

/**
 * How far along an achievement is overall: earned tiers count fully, the tier underway by its share of the way
 * there. The "Most progress" sort uses it.
 * @param achievement The achievement with its tiers and progress.
 * @returns A number from 0 to 1.
 */
export function completionRatio(achievement: AchievementDto) {
    if (achievement.tiers.length === 0) return 0;

    const next = nextTier(achievement);
    const partial = next ? Math.min(achievement.currentProgress / next.targetValue, 1) : 0;

    return (unlockedTierCount(achievement) + partial) / achievement.tiers.length;
}

/**
 * Applies the achievements tab's filters and sort. Statuses are OR-ed, categories are OR-ed, and the two groups are
 * AND-ed; an empty group doesn't filter.
 * @param achievements Every achievement of the profile.
 * @param statuses The selected status filters.
 * @param categories The selected category filters (see `CATEGORY_OPTIONS`).
 * @param sort The sort order.
 * @returns A new array; the input is not modified.
 */
export function filterAndSortAchievements(achievements: AchievementDto[], statuses: AchievementStatus[], categories: string[], sort: AchievementSort) {
    const filtered = achievements.filter(a =>
        (statuses.length === 0 || statuses.some(s => matchesStatus(a, s))) &&
        (categories.length === 0 || categories.some(c => a.categories.includes(c)))
    );

    switch (sort) {
        case 'progress': return [...filtered].sort((a, b) => completionRatio(b) - completionRatio(a));
        case 'title': return [...filtered].sort((a, b) => a.title.localeCompare(b.title));
        // Achievements with any earned tier first, the sort is stable so both groups keep the seed order
        default: return [...filtered].sort((a, b) => Number(unlockedTierCount(b) > 0) - Number(unlockedTierCount(a) > 0));
    }
}
