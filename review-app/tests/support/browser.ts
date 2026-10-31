import { vi } from 'vitest';

/**
 * Stand-ins for browser features jsdom doesn't have. Everything stubbed here is undone after each test
 * (unstubGlobals / restoreMocks in vitest.config.ts).
 */

// The app leaves the SPA with `globalThis.location.href = url` (logout, checkout, payment portal); jsdom can't navigate
export function stubLocation(href = 'https://localhost:5173/') {
    const location = { href, assign: vi.fn(), replace: vi.fn(), reload: vi.fn(), origin: new URL(href).origin };
    vi.stubGlobal('location', location);
    return location;
}

// window.confirm / alert are not implemented by jsdom (confirm returns undefined, so deletes would silently abort)
export function stubConfirm(answer: boolean) {
    const confirm = vi.fn(() => answer);
    vi.stubGlobal('confirm', confirm);
    return confirm;
}

export function stubAlert() {
    const alert = vi.fn();
    vi.stubGlobal('alert', alert);
    return alert;
}

/**
 * MUI's useMediaQuery reads matchMedia, which jsdom lacks (MUI then treats every query as not matching).
 * `matches` decides per query, e.g. q => q.includes('max-width') for "small screen" branches.
 */
export function stubMatchMedia(matches: (query: string) => boolean) {
    vi.stubGlobal('matchMedia', (query: string) => ({
        matches: matches(query),
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
    }));
}

// The services log every failure with console.error; tests of failure paths mute it (and can assert on it)
export function muteConsoleError() {
    return vi.spyOn(console, 'error').mockImplementation(() => {});
}

// document.visibilityState is read-only in jsdom
export function setVisibility(state: DocumentVisibilityState) {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
    document.dispatchEvent(new Event('visibilitychange'));
}

export function resetVisibility() {
    delete (document as any).visibilityState;
}
