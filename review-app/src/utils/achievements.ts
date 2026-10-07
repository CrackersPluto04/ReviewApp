import { AchievementDto, AchievementTierDto, AchievementTierName } from "../types/types";

export const TIER_COLORS: Record<AchievementTierName, string> = {
    Bronze: '#CD7F32',
    Silver: '#A8A9AD',
    Gold: '#D4AF37'
};

export type AchievementStatus = 'unlocked' | 'locked' | 'in-progress';
export type AchievementSort = 'default' | 'progress' | 'title';

export const STATUS_OPTIONS: { value: AchievementStatus; label: string }[] = [
    { value: 'unlocked', label: 'Unlocked' },
    { value: 'locked', label: 'Locked' },
    { value: 'in-progress', label: 'In progress' }
];

// Same names as the backend's AchievementCategories flags
export const CATEGORY_OPTIONS = ['Movie', 'Series', 'Music', 'Review', 'Reply', 'Collection'];

export const SORT_OPTIONS: { value: AchievementSort; label: string }[] = [
    { value: 'default', label: 'Unlocked first' },
    { value: 'progress', label: 'Most progress' },
    { value: 'title', label: 'Title (A-Z)' }
];

export function iconUrl(iconKey: string) {
    return `/achievements/${iconKey}.svg`;
}

export function unlockedTierCount(achievement: AchievementDto) {
    return achievement.tiers.filter(t => t.isUnlocked).length;
}

// The first tier still to earn, undefined when every tier is unlocked
export function nextTier(achievement: AchievementDto): AchievementTierDto | undefined {
    return achievement.tiers.find(t => !t.isUnlocked);
}

export function isCompleted(achievement: AchievementDto) {
    return achievement.tiers.length > 0 && nextTier(achievement) === undefined;
}

// Statuses overlap on purpose: an achievement with bronze earned and silver underway is both unlocked and in progress
export function matchesStatus(achievement: AchievementDto, status: AchievementStatus) {
    const unlocked = unlockedTierCount(achievement);

    switch (status) {
        case 'unlocked': return unlocked > 0;
        case 'locked': return unlocked === 0;
        case 'in-progress': return achievement.currentProgress > 0 && !isCompleted(achievement);
    }
}

// 0..1, earned tiers count fully, the tier underway counts by its share of the way there
export function completionRatio(achievement: AchievementDto) {
    if (achievement.tiers.length === 0) return 0;

    const next = nextTier(achievement);
    const partial = next ? Math.min(achievement.currentProgress / next.targetValue, 1) : 0;

    return (unlockedTierCount(achievement) + partial) / achievement.tiers.length;
}

// Statuses are OR-ed, categories are OR-ed, the two groups are AND-ed. An empty group doesn't filter.
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
