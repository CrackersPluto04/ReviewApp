import { AuthUser } from "../context/AuthContext";
import { PlanDto } from "../types/types";
import { TIER_COLORS } from "./achievements";

// The same gold as the achievement tiers, readable on both light and dark backgrounds
export const PREMIUM_COLOR = TIER_COLORS.Gold;

// Keep in sync with FeatureKeys in the backend (Services/Entitlements.cs)
export const FEATURE_KEYS = {
    profileBadge: 'profile-badge',
    entitlementPreview: 'entitlement-preview'
} as const;

// For showing / hiding UI only: the backend decides access on every request (402 when missing)
export function hasFeature(user: AuthUser | null, featureKey: string): boolean {
    return user?.entitlements?.includes(featureKey) ?? false;
}

// e.g. "€4.99 / month"
export function formatPrice(plan: PlanDto): string {
    const price = new Intl.NumberFormat(undefined, { style: 'currency', currency: plan.currency }).format(plan.priceAmount);
    return `${price} / ${plan.interval}`;
}

// Stored in UTC, shown as a local date
export function formatSubscriptionDate(isoDate?: string | null): string {
    if (!isoDate) return '';
    return new Date(isoDate).toLocaleDateString(undefined, { dateStyle: 'long' });
}
