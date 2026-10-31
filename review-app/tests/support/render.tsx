import { act, render } from '@testing-library/preact';
import userEvent from '@testing-library/user-event';
import { ComponentChildren } from 'preact';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { AppProviders, AppRoutes, useThemeMode } from '../../src/App';
import { AuthUser } from '../../src/context/AuthContext';
import { PremiumUpsellProvider } from '../../src/context/PremiumUpsellContext';
import { server } from './server';
import { apiUrl } from './api';

/**
 * userEvent that also works under fake timers: its internal delays advance the fake clock.
 * Use with vi.useFakeTimers({ shouldAdvanceTime: true }) (see useFakeTimers below) so findBy / waitFor keep polling.
 */
export function setupUser() {
    return userEvent.setup({
        advanceTimers: ms => { if (vi.isFakeTimers()) vi.advanceTimersByTime(ms); },
    });
}

/**
 * Fake timers that still move with real time, so Testing Library's polling (findBy / waitFor) keeps working.
 * `frozen` stops the clock completely, for exact timing assertions ("nothing before 300 ms"): then only
 * vi.advanceTimersByTimeAsync moves it, and findBy / waitFor can't be used (they would wait on the frozen clock).
 * Frozen also fakes requestAnimationFrame, which Preact runs effects after: call paint() after an interaction.
 */
export function useFakeTimers(now?: string | number | Date, { frozen = false } = {}) {
    vi.useFakeTimers({
        shouldAdvanceTime: !frozen,
        toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date',
            ...(frozen ? ['requestAnimationFrame', 'cancelAnimationFrame'] as const : [])],
    });
    if (now !== undefined) vi.setSystemTime(now);
}

// With frozen fake timers: lets pending responses and renders through without moving the clock
export async function flush() {
    for (let i = 0; i < 5; i++)
        await act(() => new Promise<void>(resolve => setImmediate(resolve)));
}

// With frozen fake timers: Preact runs effects after the next frame or 35 ms at the latest; this lets them run (their timers start then)
export async function paint() {
    await act(async () => { await vi.advanceTimersByTimeAsync(50); });
    await flush();
}

// The user AuthProvider gets from check-auth (null = anonymous)
export function loginAs(user: AuthUser | null) {
    server.use(http.get(apiUrl('Auth/check-auth'), () => user
        ? HttpResponse.json(user)
        : HttpResponse.json({ error: 'Unauthorized' }, { status: 401 })));
}

// Shows where the router is, so tests can assert navigation
function LocationProbe() {
    const location = useLocation();
    return <div data-testid="location" data-state={JSON.stringify(location.state ?? null)}>{location.pathname + location.search}</div>;
}

export function currentLocation() {
    return document.querySelector('[data-testid="location"]')?.textContent ?? '';
}

export function currentLocationState() {
    return JSON.parse(document.querySelector('[data-testid="location"]')?.getAttribute('data-state') ?? 'null');
}

function TestProviders({ route, children }: { route: string | { pathname: string; state?: unknown }; children: ComponentChildren }) {
    const { mode } = useThemeMode();

    return <AppProviders mode={mode}>
        <MemoryRouter initialEntries={[route]}>
            <PremiumUpsellProvider>
                {children}
                <LocationProbe />
            </PremiumUpsellProvider>
        </MemoryRouter>
    </AppProviders>
}

interface RenderOptions {
    // Logged-in user (check-auth answers with it), null = anonymous; omitted = the current check-auth handler (anonymous by default)
    user?: AuthUser | null;
    // The URL the router starts at, may carry router state
    route?: string | { pathname: string; state?: unknown };
    // The route pattern the element is mounted at, so useParams works (e.g. '/media/:mediaType/:externalApiId')
    path?: string;
}

/**
 * Renders a component inside the same providers as the app (theme, auth, notifications, toasts, router, upsell).
 * Other paths render nothing, so navigation away is visible through currentLocation().
 */
export function renderWithApp(ui: ComponentChildren, { user, route = '/', path }: RenderOptions = {}) {
    if (user !== undefined) loginAs(user);

    const result = render(
        <TestProviders route={route}>
            <Routes>
                <Route path={path ?? '*'} element={ui} />
                {path && <Route path="*" element={null} />}
            </Routes>
        </TestProviders>
    );

    return { ...result, user: setupUser() };
}

// The whole app (real route table and page chrome) at the given URL, like App but with a memory router
export function renderApp(route: string | { pathname: string; state?: unknown }, { user }: { user?: AuthUser | null } = {}) {
    if (user !== undefined) loginAs(user);

    function Tree() {
        const { mode, toggleTheme } = useThemeMode();
        return <AppProviders mode={mode}>
            <MemoryRouter initialEntries={[route]}>
                <AppRoutes mode={mode} toggleTheme={toggleTheme} />
                <LocationProbe />
            </MemoryRouter>
        </AppProviders>
    }

    const result = render(<Tree />);
    return { ...result, user: setupUser() };
}
