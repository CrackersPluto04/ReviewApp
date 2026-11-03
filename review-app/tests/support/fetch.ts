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

        const response = init?.body instanceof FormData
            ? encodeFormData(init.body).then(({ body, contentType }) => {
                const headers = new Headers(init.headers);
                headers.set('content-type', contentType);
                return mswFetch(input, { ...init, body, headers });
            })
            : mswFetch(input, init);
        const settled = response.catch(() => undefined).finally(() => pending.delete(settled));
        pending.add(settled);
        return response;
    };
}

/**
 * Uploads (camera recognition) send a FormData with a Blob. Under jsdom both are jsdom's classes, which Node's fetch
 * can't serialize (and MSW can't take a streamed multipart body either), so the body is encoded here, the way a browser
 * would: multipart/form-data with a boundary, the same names, file names, types and bytes.
 */
async function encodeFormData(form: FormData): Promise<{ body: ArrayBuffer, contentType: string }> {
    const boundary = `----TestFormBoundary${Math.random().toString(16).slice(2)}`;
    const text = new TextEncoder();
    const parts: Uint8Array[] = [];

    for (const [name, value] of form.entries()) {
        if (typeof value === 'string') {
            parts.push(text.encode(`--${boundary}\r\nContent-Disposition: form-data; name="${name}"\r\n\r\n${value}\r\n`));
            continue;
        }
        const file = value as File;
        parts.push(text.encode(`--${boundary}\r\nContent-Disposition: form-data; name="${name}"; filename="${file.name ?? 'blob'}"\r\n`
            + `Content-Type: ${file.type || 'application/octet-stream'}\r\n\r\n`));
        parts.push(new Uint8Array(await file.arrayBuffer()));
        parts.push(text.encode('\r\n'));
    }
    parts.push(text.encode(`--${boundary}--\r\n`));

    const body = new ArrayBuffer(parts.reduce((length, part) => length + part.length, 0));
    const bytes = new Uint8Array(body);
    let offset = 0;
    for (const part of parts) {
        bytes.set(part, offset);
        offset += part.length;
    }
    return { body, contentType: `multipart/form-data; boundary=${boundary}` };
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
