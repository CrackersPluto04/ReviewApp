import { AuthUser } from "../context/AuthContext";
import { PlanDto } from "../types/types";
import { TIER_COLORS } from "./achievements";

/** The Premium accent: the same gold as the achievement tiers, readable on both light and dark backgrounds. */
export const PREMIUM_COLOR = TIER_COLORS.Gold;

/**
 * The feature keys the web UI checks. Keep in sync with `FeatureKeys` in the backend (`Services/Entitlements.cs`).
 * Not every backend key is listed: `offline-mode` is enforced on the Android app only.
 */
export const FEATURE_KEYS = {
    profileBadge: 'profile-badge',
    cameraRecognition: 'camera-recognition'
} as const;

/**
 * Whether the user's entitlements include a feature. For showing or hiding UI only: the backend decides access
 * on every request (402 when missing), so a stale value here never grants anything.
 * @param user The logged-in user, or `null` when logged out.
 * @param featureKey One of `FEATURE_KEYS`.
 */
export function hasFeature(user: AuthUser | null, featureKey: string): boolean {
    return user?.entitlements?.includes(featureKey) ?? false;
}

/**
 * The plan's price line, e.g. "€4.99 / month". Must match the price at the payment provider; the API only reports it.
 * @param plan The plan from `SubscriptionService.getPlan`.
 */
export function formatPrice(plan: PlanDto): string {
    const price = new Intl.NumberFormat(undefined, { style: 'currency', currency: plan.currency }).format(plan.priceAmount);
    return `${price} / ${plan.interval}`;
}

/**
 * Formats a subscription date. Stored in UTC, shown as a local date. Such a date can end with a dot in some
 * locales, so it should never end a sentence.
 * @param isoDate The ISO 8601 timestamp from the API.
 * @returns The long date, or an empty string when there is none.
 */
export function formatSubscriptionDate(isoDate?: string | null): string {
    if (!isoDate) return '';
    return new Date(isoDate).toLocaleDateString(undefined, { dateStyle: 'long' });
}
