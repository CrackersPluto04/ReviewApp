/**
 * A thin layer over the fetch MSW patched:
 * - records what the app passed to fetch, because the Request MSW hands to handlers doesn't keep `credentials`
 *   (capture() in api.ts reads it from here);
 * - rejects every request once the file's server is closed, so a late request (e.g. a refresh started
 *   by the last test) fails like an offline network instead of reaching a real localhost:7140.
 */
interface SentRequest {
    method: string;
    url: string;
    credentials: RequestCredentials | undefined;
}

const sent: SentRequest[] = [];
const pending = new Set<Promise<unknown>>();
let networkOpen = false;

export function installFetchRecorder() {
    const mswFetch = globalThis.fetch;
    networkOpen = true;

    globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
        if (!networkOpen)
            return Promise.reject(new TypeError('fetch failed: the test network is closed'));

        const url = input instanceof Request ? input.url : String(input);
        const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
        sent.push({ method, url, credentials: init?.credentials ?? (input instanceof Request ? input.credentials : undefined) });

        const response = mswFetch(input, init);
        const settled = response.catch(() => undefined).finally(() => pending.delete(settled));
        pending.add(settled);
        return response;
    };
}

/**
 * Waits for the requests still running (e.g. a refresh a test's last click started), so they are answered
 * by that test's handlers, not the next test's or a closed server's. Bounded, for responses a test never released.
 */
export async function settlePendingRequests(timeoutMs = 1000) {
    if (pending.size === 0) return;
    await Promise.race([
        Promise.allSettled([...pending]),
        new Promise(resolve => setTimeout(resolve, timeoutMs)),
    ]);
}

// Call after server.close(): MSW puts the native fetch back when it closes
export function closeNetwork() {
    networkOpen = false;
    globalThis.fetch = () => Promise.reject(new TypeError('fetch failed: the test network is closed'));
}

// The oldest unclaimed fetch call matching the request MSW is answering
export function claimSentRequest(method: string, url: string): SentRequest | undefined {
    const index = sent.findIndex(s => s.method === method && s.url === url);
    return index === -1 ? undefined : sent.splice(index, 1)[0];
}

export function clearSentRequests() {
    sent.length = 0;
}
