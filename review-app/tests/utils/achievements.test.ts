import { describe, expect, it } from 'vitest';
import {
    completionRatio, filterAndSortAchievements, iconUrl, isCompleted, matchesStatus, nextTier, unlockedTierCount,
} from '../../src/utils/achievements';
import { achievement } from '../support/builders';

// Tiers at 1 / 10 / 50 (see the builder)
const none = achievement({ title: 'None', currentProgress: 0 });
const started = achievement({ title: 'Started', currentProgress: 0, tiers: achievement({ currentProgress: 0 }).tiers });
const bronze = achievement({ title: 'Bronze', currentProgress: 4 });
const silver = achievement({ title: 'Silver', currentProgress: 10 });
const gold = achievement({ title: 'Gold', currentProgress: 60 });

describe('achievements utils', () => {
    it('builds the icon path from the key', () => {
        expect(iconUrl('critic')).toBe('/achievements/critic.svg');
    });

    it('counts the unlocked tiers and finds the next one', () => {
        expect(unlockedTierCount(none)).toBe(0);
        expect(nextTier(none)?.tier).toBe('Bronze');

        expect(unlockedTierCount(bronze)).toBe(1);
        expect(nextTier(bronze)?.tier).toBe('Silver');

        expect(unlockedTierCount(gold)).toBe(3);
        expect(nextTier(gold)).toBeUndefined();
    });

    it('is completed only when every tier is unlocked, never without tiers', () => {
        expect(isCompleted(gold)).toBe(true);
        expect(isCompleted(silver)).toBe(false);
        expect(isCompleted(achievement({ tiers: [] }))).toBe(false);
    });

    describe('matchesStatus', () => {
        it('unlocked = at least one tier earned', () => {
            expect(matchesStatus(none, 'unlocked')).toBe(false);
            expect(matchesStatus(bronze, 'unlocked')).toBe(true);
            expect(matchesStatus(gold, 'unlocked')).toBe(true);
        });

        it('locked = nothing earned yet', () => {
            expect(matchesStatus(none, 'locked')).toBe(true);
            expect(matchesStatus(bronze, 'locked')).toBe(false);
        });

        it('in progress = some progress and not completed, overlapping with unlocked', () => {
            expect(matchesStatus(none, 'in-progress')).toBe(false);
            expect(matchesStatus(bronze, 'in-progress')).toBe(true);
            expect(matchesStatus(gold, 'in-progress')).toBe(false);

            const halfwayToBronze = achievement({ currentProgress: 1, tiers: [
                { tier: 'Bronze', description: '', targetValue: 5, isUnlocked: false },
            ] });
            expect(matchesStatus(halfwayToBronze, 'in-progress')).toBe(true);
            expect(matchesStatus(halfwayToBronze, 'locked')).toBe(true);
        });
    });

    describe('completionRatio', () => {
        it('counts earned tiers fully and the tier underway by its share', () => {
            expect(completionRatio(none)).toBe(0);
            // 1 of 3 tiers + 4/10 of silver
            expect(completionRatio(bronze)).toBeCloseTo((1 + 0.4) / 3);
            // 2 of 3 tiers + 10/50 of gold
            expect(completionRatio(silver)).toBeCloseTo((2 + 0.2) / 3);
            expect(completionRatio(gold)).toBe(1);
        });

        it('caps the partial share at a whole tier and is 0 without tiers', () => {
            const overshoot = achievement({ currentProgress: 99, tiers: [
                { tier: 'Bronze', description: '', targetValue: 5, isUnlocked: false },
            ] });
            expect(completionRatio(overshoot)).toBe(1);
            expect(completionRatio(achievement({ tiers: [] }))).toBe(0);
        });
    });

    describe('filterAndSortAchievements', () => {
        const all = [none, bronze, silver, gold];
        const titles = (list: { title: string }[]) => list.map(a => a.title);

        it('returns everything in the default order without filters', () => {
            // Unlocked first, stable within each group
            expect(titles(filterAndSortAchievements(all, [], [], 'default'))).toEqual(['Bronze', 'Silver', 'Gold', 'None']);
        });

        it('ORs the statuses', () => {
            expect(titles(filterAndSortAchievements(all, ['locked'], [], 'default'))).toEqual(['None']);
            expect(titles(filterAndSortAchievements(all, ['locked', 'in-progress'], [], 'default'))).toEqual(['Bronze', 'Silver', 'None']);
        });

        it('ORs the categories and ANDs them with the statuses', () => {
            const movie = achievement({ title: 'Movie fan', categories: ['Movie', 'Review'], currentProgress: 1 });
            const music = achievement({ title: 'Music fan', categories: ['Music'], currentProgress: 0 });
            const list = [movie, music];

            expect(titles(filterAndSortAchievements(list, [], ['Music'], 'default'))).toEqual(['Music fan']);
            expect(titles(filterAndSortAchievements(list, [], ['Music', 'Review'], 'title'))).toEqual(['Movie fan', 'Music fan']);
            expect(titles(filterAndSortAchievements(list, ['unlocked'], ['Music'], 'default'))).toEqual([]);
        });

        it('sorts by progress, most first', () => {
            expect(titles(filterAndSortAchievements(all, [], [], 'progress'))).toEqual(['Gold', 'Silver', 'Bronze', 'None']);
        });

        it('sorts by title alphabetically', () => {
            expect(titles(filterAndSortAchievements([silver, none, gold, bronze], [], [], 'title'))).toEqual(['Bronze', 'Gold', 'None', 'Silver']);
        });

        it('does not change the input array', () => {
            const input = [none, gold];
            filterAndSortAchievements(input, [], [], 'progress');
            expect(input).toEqual([none, gold]);
        });

        it('keeps the given order for equal entries', () => {
            expect(titles(filterAndSortAchievements([started, none], [], [], 'default'))).toEqual(['Started', 'None']);
        });
    });
});
