/**
 * Thin wrapper around fetch used by every service.
 * A 401 means the auth cookie is missing, expired or revoked (e.g. after a password change on another device),
 * so the registered handler (AuthContext) can drop the logged-in state instead of the UI pretending to be logged in.
 */

let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
    unauthorizedHandler = handler;
}

export async function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
    const response = await fetch(input, init);

    if (response.status === 401)
        unauthorizedHandler?.();

    return response;
}
