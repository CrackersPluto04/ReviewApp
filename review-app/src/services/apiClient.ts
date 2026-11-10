import { UnlockedAchievementDto } from "../types/types";

/**
 * @module apiClient
 * The one place every service sends its requests through, so that the app-wide reactions to a response
 * (logout, upsell, toasts, notification refresh) need no code in the individual services or components.
 * The reactions are registered as handlers by the contexts that own them; without a handler a response is just passed on.
 */

const UNLOCKED_ACHIEVEMENTS_HEADER = 'X-Unlocked-Achievements';

let unauthorizedHandler: (() => void) | null = null;
let unlockHandler: ((unlocked: UnlockedAchievementDto[]) => void) | null = null;
let mutationHandler: (() => void) | null = null;
let premiumRequiredHandler: (() => void) | null = null;

/**
 * Registers what happens on a 401: the auth cookie is missing, expired or revoked (e.g. after a password change
 * on another device), so the logged-in state must be dropped instead of the UI pretending to be logged in.
 * Set by `AuthContext`.
 * @param handler The callback, or `null` to unregister.
 */
export function setUnauthorizedHandler(handler: (() => void) | null) {
    unauthorizedHandler = handler;
}

/**
 * Registers what happens on a 402: the endpoint is a Premium feature, or the subscription just ended. The handler
 * refreshes the user and offers the plans page; the caller still receives the response.
 * Set by `PremiumUpsellContext`.
 * @param handler The callback, or `null` to unregister.
 */
export function setPremiumRequiredHandler(handler: (() => void) | null) {
    premiumRequiredHandler = handler;
}

/**
 * Registers what happens when a response carries newly unlocked achievement tiers (any action can unlock them).
 * Set by `AchievementToastContext`, which shows the toasts.
 * @param handler Receives the unlocked tiers, or `null` to unregister.
 */
export function setUnlockHandler(handler: ((unlocked: UnlockedAchievementDto[]) => void) | null) {
    unlockHandler = handler;
}

/**
 * Registers what happens after every successful change (non-GET request): the user's own actions can create
 * notifications for them (achievements, account changes), so the unseen count is refreshed.
 * Set by `NotificationContext`.
 * @param handler The callback, or `null` to unregister.
 */
export function setMutationHandler(handler: (() => void) | null) {
    mutationHandler = handler;
}

/**
 * `fetch` plus the app-wide response reactions (see the setters above). Use it instead of plain `fetch` in every service.
 * It never alters or consumes the response, so callers read the body as usual.
 * @param input The URL or request, as for `fetch`.
 * @param init The request options, as for `fetch`. Requests with `credentials: 'include'` carry the httpOnly auth cookie.
 * @returns The unchanged response. Rejects only when the network request itself fails.
 */
export async function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
    const response = await fetch(input, init);

    if (response.status === 401)
        unauthorizedHandler?.();

    if (response.status === 402)
        premiumRequiredHandler?.();

    const unlockedHeader = response.headers.get(UNLOCKED_ACHIEVEMENTS_HEADER);
    if (unlockedHeader)
        notifyUnlocked(unlockedHeader);

    const method = init?.method?.toUpperCase() ?? 'GET';
    if (response.ok && method !== 'GET')
        mutationHandler?.();

    return response;
}

// A malformed header must never break the request it came with
function notifyUnlocked(headerValue: string) {
    try {
        const unlocked = JSON.parse(headerValue);
        if (Array.isArray(unlocked) && unlocked.length > 0)
            unlockHandler?.(unlocked);
    } catch (error) {
        console.error('Could not read unlocked achievements:', error);
    }
}
