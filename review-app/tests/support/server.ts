import { setupServer } from 'msw/node';
import { http, HttpResponse } from 'msw';
import { apiUrl } from './api';

/**
 * The fake backend: every request of a test is answered by MSW, nothing leaves the machine.
 * The default handlers cover the requests the global providers make on their own
 * (AuthProvider's check-auth, NotificationProvider's unseen count), tests add theirs with server.use().
 */
export const defaultHandlers = [
    // Anonymous by default, renderWithApp({ user }) logs a user in
    http.get(apiUrl('Auth/check-auth'), () => HttpResponse.json({ error: 'Unauthorized' }, { status: 401 })),
    http.get(apiUrl('Notification/unseen-count'), () => HttpResponse.json({ count: 0 })),
];

export const server = setupServer(...defaultHandlers);

// Requests without a handler. The services turn a failed fetch into a "Network error" result instead of throwing,
// so MSW's own 'error' strategy would not fail the test: the setup file fails it after the test instead.
const unhandledRequests: string[] = [];

export function recordUnhandledRequest(request: Request) {
    unhandledRequests.push(`${request.method} ${request.url}`);
}

export function takeUnhandledRequests() {
    return unhandledRequests.splice(0);
}
