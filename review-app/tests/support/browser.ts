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

/**
 * A webcam for getUserMedia (jsdom has none). `error` makes access fail with that DOMException name
 * (e.g. 'NotAllowedError'), `cameras` is how many enumerateDevices lists. Undo it with removeCamera().
 */
export function stubCamera({ error, cameras = 1 }: { error?: string, cameras?: number } = {}) {
    const track = { stop: vi.fn() };
    const stream = { getTracks: () => [track] };
    const getUserMedia = vi.fn(async () => {
        if (error) throw new DOMException('Camera failed', error);
        return stream;
    });
    const enumerateDevices = vi.fn(async () =>
        Array.from({ length: cameras }, (_, i) => ({ kind: 'videoinput', deviceId: `camera-${i}`, label: `Camera ${i}` })));

    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia, enumerateDevices } });
    vi.stubGlobal('isSecureContext', true);
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
    return { getUserMedia, enumerateDevices, track };
}

export function removeCamera() {
    delete (navigator as any).mediaDevices;
}

// URL.createObjectURL / revokeObjectURL (photo previews) are missing from jsdom
export function stubObjectUrls() {
    const create = vi.fn(() => 'blob:photo-preview');
    const revoke = vi.fn();
    Object.assign(URL, { createObjectURL: create, revokeObjectURL: revoke });
    return { create, revoke };
}

// document.visibilityState is read-only in jsdom
export function setVisibility(state: DocumentVisibilityState) {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
    document.dispatchEvent(new Event('visibilitychange'));
}

export function resetVisibility() {
    delete (document as any).visibilityState;
}
