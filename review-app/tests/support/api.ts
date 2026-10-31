import { http, HttpResponse, JsonBodyType } from 'msw';
import { claimSentRequest } from './fetch';

// The services hard-code this base URL (see src/services/*.ts)
export const API_BASE = 'https://localhost:7140/api';

export const apiUrl = (path: string) => `${API_BASE}/${path}`;

type Method = 'get' | 'post' | 'put' | 'patch' | 'delete';

export interface CapturedRequest {
    method: string;
    url: URL;
    // Parsed JSON body, the raw text when it isn't JSON, undefined without a body
    body: any;
    // What the app passed to fetch (undefined = the browser default, cookies not sent cross-origin)
    credentials: RequestCredentials | undefined;
    headers: Headers;
}

type Responder = (request: CapturedRequest, params: Record<string, string | readonly string[] | undefined>) => Response | Promise<Response>;

/**
 * A handler that records every request it answers, so tests can assert what the app sent.
 * `respond` is a Response (cloned for every call) or a function of the request.
 */
export function capture(method: Method, path: string, respond: Response | Responder = HttpResponse.json({})) {
    const calls: CapturedRequest[] = [];

    const handler = http[method](apiUrl(path), async ({ request, params }) => {
        const text = await request.clone().text();
        let body: any = undefined;
        if (text) {
            try { body = JSON.parse(text); } catch { body = text; }
        }

        const captured: CapturedRequest = {
            method: request.method,
            url: new URL(request.url),
            body,
            credentials: claimSentRequest(request.method, request.url)?.credentials,
            headers: request.headers,
        };
        calls.push(captured);

        return typeof respond === 'function' ? respond(captured, params) : respond.clone();
    });

    return {
        handler,
        calls,
        get last() { return calls[calls.length - 1]; },
    };
}

/* Responses the backend gives */

export const ok = <T extends JsonBodyType>(body: T, init?: ResponseInit) => HttpResponse.json(body, init);

export const noContent = () => new HttpResponse(null, { status: 204 });

// Our controllers' error shape
export const apiError = (status: number, error: string) => HttpResponse.json({ error }, { status });

// What [ApiController] model validation returns (ValidationProblemDetails)
export const validationProblem = (errors: Record<string, string[]>) =>
    HttpResponse.json({ title: 'One or more validation errors occurred.', status: 400, errors }, { status: 400 });

// An error page from a proxy or the server itself, not JSON
export const htmlError = (status: number) =>
    new HttpResponse('<html><body>Internal Server Error</body></html>', { status, headers: { 'Content-Type': 'text/html' } });

// The request never reaches the server (offline, DNS, CORS): fetch rejects
export const networkError = () => HttpResponse.error();

export const unauthorized = () => apiError(401, 'Unauthorized');

export const paymentRequired = (feature = 'entitlement-preview') =>
    HttpResponse.json({ error: 'This is a Premium feature.', code: 'premium_required', feature }, { status: 402 });

/**
 * A response the test releases by hand, to assert loading states and races.
 * `respond` is the answer used by release() when no other is given.
 */
export function deferred(respond: () => Response = () => HttpResponse.json({})) {
    let release!: (response?: Response) => void;
    const promise = new Promise<Response>(resolve => {
        release = (response?: Response) => resolve(response ?? respond());
    });

    return { promise, release: (response?: Response) => release(response) };
}
