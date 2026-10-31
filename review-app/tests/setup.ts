import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/preact';
import { afterAll, afterEach, beforeAll, vi } from 'vitest';
import { recordUnhandledRequest, server, takeUnhandledRequests } from './support/server';
import { pinLocaleAndTimeZone } from './support/locale';
import { resetVisibility } from './support/browser';
import { clearSentRequests, closeNetwork, installFetchRecorder, settlePendingRequests } from './support/fetch';
import {
    setMutationHandler, setPremiumRequiredHandler, setUnauthorizedHandler, setUnlockHandler,
} from '../src/services/apiClient';

pinLocaleAndTimeZone();

// jsdom has no layout: scrolling is a no-op the tests can spy on
window.scrollTo = vi.fn() as any;
Element.prototype.scrollIntoView = vi.fn();

beforeAll(() => {
    server.listen({
        // Recorded (the test fails after it) and errored, so it never reaches the real network
        onUnhandledFrame({ frame, defaults }) {
            const request = (frame.data as { request?: Request }).request;
            if (request) recordUnhandledRequest(request);
            defaults.error();
        },
    });
    installFetchRecorder();
});

afterEach(async () => {
    // Unmount first, so effects and timers of the test's components stop before anything else is reset
    cleanup();
    vi.useRealTimers();
    await settlePendingRequests();
    server.resetHandlers();

    // The providers register these on mount and clear them on unmount; reset anyway for tests that set them directly
    setUnauthorizedHandler(null);
    setPremiumRequiredHandler(null);
    setUnlockHandler(null);
    setMutationHandler(null);

    clearSentRequests();
    localStorage.clear();
    sessionStorage.clear();
    resetVisibility();

    const unhandled = takeUnhandledRequests();
    if (unhandled.length > 0)
        throw new Error(`Requests without a handler (add one with server.use):\n  ${unhandled.join('\n  ')}`);
});

afterAll(() => {
    server.close();
    closeNetwork();
});
