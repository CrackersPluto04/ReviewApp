import { describe, expect, it } from 'vitest';
import {
    allowanceText, emptyResultMessage, formatResetTime, itemKey, mergeItems, TIER_LABELS, tierLabel
} from '../../src/utils/recognition';
import { recognizedMedia } from '../support/builders';

describe('recognition utils', () => {
    it('names every tier for users', () => {
        expect(TIER_LABELS).toEqual({
            direct: 'Direct match',
            universe: 'Same universe',
            people: 'Related people',
            similar: 'You might also like',
        });
        expect(tierLabel('people')).toBe('Related people');
        // A tier the app doesn't know yet reads as the loosest one
        expect(tierLabel('cousins')).toBe('You might also like');
    });

    it('explains each empty outcome', () => {
        expect(emptyResultMessage('nothing_recognised')).toBe("We couldn't recognise anything in this photo.");
        expect(emptyResultMessage('not_media_related')).toMatch(/doesn't look related/);
        expect(emptyResultMessage('person_not_identified')).toMatch(/People are not identified/);
        expect(emptyResultMessage('recognised')).toMatch(/couldn't find matching/);
    });

    describe('mergeItems', () => {
        it('appends a page, skipping items already on screen', () => {
            const a = recognizedMedia('direct', { externalApiID: 'a' });
            const b = recognizedMedia('universe', { externalApiID: 'b' });
            const sameIdOtherType = recognizedMedia('people', { externalApiID: 'a', mediaType: 1 });

            const merged = mergeItems([a], [{ ...a }, b, sameIdOtherType]);

            expect(merged.map(itemKey)).toEqual(['0:a', '0:b', '1:a']);
        });

        it('returns a new list', () => {
            const current = [recognizedMedia()];
            expect(mergeItems(current, [])).not.toBe(current);
        });
    });

    it('describes the allowance', () => {
        expect(allowanceText(14, 20)).toBe('14 of 20 left today');
        expect(allowanceText(null, 20)).toBe('');
        expect(allowanceText(3, undefined)).toBe('');
    });

    it('formats the reset time in local time', () => {
        // The tests run in UTC
        expect(formatResetTime('2026-11-03T00:00:00Z')).toBe('12:00 AM');
        expect(formatResetTime(null)).toBe('');
    });
});
