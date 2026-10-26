import { UnlockedAchievementDto } from "../types/types";

/**
 * Thin wrapper around fetch used by every service.
 * A 401 means the auth cookie is missing, expired or revoked (e.g. after a password change on another device),
 * so the registered handler (AuthContext) can drop the logged-in state instead of the UI pretending to be logged in.
 * Any action that unlocks achievement tiers sends them in the X-Unlocked-Achievements header,
 * which is passed to the registered handler (AchievementToastContext) to show a toast.
 * Every successful change (non-GET request) is reported to the mutation handler (NotificationContext),
 * since the user's own actions can create notifications for them (achievements, account changes).
 * A 402 means the endpoint is a Premium feature (or the subscription just ended): the registered handler
 * (PremiumUpsellContext) refreshes the user and offers the plans page. The caller still gets the response.
 */

const UNLOCKED_ACHIEVEMENTS_HEADER = 'X-Unlocked-Achievements';

let unauthorizedHandler: (() => void) | null = null;
let unlockHandler: ((unlocked: UnlockedAchievementDto[]) => void) | null = null;
let mutationHandler: (() => void) | null = null;
let premiumRequiredHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
    unauthorizedHandler = handler;
}

export function setPremiumRequiredHandler(handler: (() => void) | null) {
    premiumRequiredHandler = handler;
}

export function setUnlockHandler(handler: ((unlocked: UnlockedAchievementDto[]) => void) | null) {
    unlockHandler = handler;
}

export function setMutationHandler(handler: (() => void) | null) {
    mutationHandler = handler;
}

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
