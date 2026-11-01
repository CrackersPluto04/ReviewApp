import { describe, expect, it } from 'vitest';
import { FEATURE_KEYS, formatPrice, formatSubscriptionDate, hasFeature, PREMIUM_COLOR } from '../../src/utils/subscription';
import { TIER_COLORS } from '../../src/utils/achievements';
import { authUser, plan, premiumAuthUser } from '../support/builders';

describe('subscription utils', () => {
    it('uses the gold tier colour for Premium', () => {
        expect(PREMIUM_COLOR).toBe(TIER_COLORS.Gold);
    });

    it('keeps the feature keys in sync with the backend', () => {
        expect(FEATURE_KEYS).toEqual({ profileBadge: 'profile-badge', entitlementPreview: 'entitlement-preview' });
    });

    describe('hasFeature', () => {
        it('is true only for entitled users', () => {
            expect(hasFeature(premiumAuthUser(), FEATURE_KEYS.entitlementPreview)).toBe(true);
            expect(hasFeature(authUser(), FEATURE_KEYS.entitlementPreview)).toBe(false);
            expect(hasFeature(null, FEATURE_KEYS.entitlementPreview)).toBe(false);
        });

        it('tolerates a user without the entitlements list', () => {
            expect(hasFeature({ ...authUser(), entitlements: undefined as any }, 'x')).toBe(false);
        });
    });

    describe('formatPrice', () => {
        it('formats the amount in the plan currency with the interval', () => {
            expect(formatPrice(plan({ priceAmount: 4.99, currency: 'EUR', interval: 'month' }))).toBe('€4.99 / month');
            expect(formatPrice(plan({ priceAmount: 10, currency: 'USD', interval: 'year' }))).toBe('$10.00 / year');
        });
    });

    describe('formatSubscriptionDate', () => {
        it('formats as a long local date', () => {
            expect(formatSubscriptionDate('2026-12-01T00:00:00Z')).toBe('December 1, 2026');
        });

        it('is empty without a date', () => {
            expect(formatSubscriptionDate(null)).toBe('');
            expect(formatSubscriptionDate(undefined)).toBe('');
            expect(formatSubscriptionDate('')).toBe('');
        });
    });
});
