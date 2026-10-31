import { describe, expect, it } from 'vitest';
import { HttpResponse } from 'msw';
import { server } from '../support/server';
import { apiError, capture, CapturedRequest, htmlError, networkError, validationProblem } from '../support/api';
import { muteConsoleError } from '../support/browser';
import { knownIssue } from '../support/known-issue';

type Method = 'get' | 'post' | 'put' | 'patch' | 'delete';

const SESSION_EXPIRED = 'Your session has expired. Please log in again.';

export interface EndpointCase {
    name: string;
    method: Method;
    // MSW path under the API base, e.g. 'Collection/5' or 'Review/media/0/ext-1'
    path: string;
    call: () => Promise<any>;
    // Extra checks of what was sent (body, query)
    request?: (request: CapturedRequest) => void;
    // What fetch got as `credentials`: 'include' when left out; anonymous endpoints pass `credentials: undefined`
    credentials?: RequestCredentials | undefined;
    // A successful answer and the result the service makes of it
    response?: Response;
    result: object;
    // The message when the API gives no usable error, and when fetch rejects
    fallback: string;
    network: string;
    // The error body is read with .json().catch(): a non-JSON error (e.g. a proxy's HTML page) still gives the fallback.
    // false = an unguarded .json() turns it into the network message (known issue W2)
    safeErrorBody?: boolean;
    // Reads the first ValidationProblemDetails message
    readsValidationProblems?: boolean;
    // A 401 gives "session expired" instead of the API's text
    explainsUnauthorized?: boolean;
    // Extra fields of every failure result (e.g. SubscriptionService's status)
    failureExtras?: (status: number) => object;
}

/**
 * The standard tests of one service method: the request it sends, its success result and every failure path.
 * The services never throw: each failure comes back as { success: false, message }.
 */
export function describeEndpoint(c: EndpointCase) {
    const serve = (response: Response) => {
        const endpoint = capture(c.method, c.path, response);
        server.use(endpoint.handler);
        return endpoint;
    };

    const failure = (message: string, status?: number) => ({
        success: false,
        message,
        ...(status !== undefined && c.failureExtras ? c.failureExtras(status) : {}),
    });

    describe(c.name, () => {
        it(`sends ${c.method.toUpperCase()} ${c.path}`, async () => {
            const endpoint = serve(c.response ?? HttpResponse.json({}));

            await c.call();

            expect(endpoint.calls).toHaveLength(1);
            expect(endpoint.last.credentials).toBe('credentials' in c ? c.credentials : 'include');
            c.request?.(endpoint.last);
        });

        it('returns the result on success', async () => {
            serve(c.response ?? HttpResponse.json({}));

            expect(await c.call()).toEqual(c.result);
        });

        it('returns the API\'s error message', async () => {
            serve(apiError(409, 'The API said no.'));

            expect(await c.call()).toEqual(failure('The API said no.', 409));
        });

        it('falls back to its own message when the error has none', async () => {
            serve(HttpResponse.json({}, { status: 400 }));

            expect(await c.call()).toEqual(failure(c.fallback, 400));
        });

        it('returns the network message when the request fails', async () => {
            muteConsoleError();
            serve(networkError());

            expect(await c.call()).toEqual(failure(c.network));
        });

        if (c.safeErrorBody) {
            it('falls back to its own message for an error page that isn\'t JSON', async () => {
                serve(htmlError(502));

                expect(await c.call()).toEqual(failure(c.fallback, 502));
            });
        } else {
            knownIssue('W2', `${c.name}: an error page that isn't JSON gives the error fallback, not the network message`, async () => {
                muteConsoleError();
                serve(htmlError(502));

                expect(await c.call()).toEqual(failure(c.fallback, 502));
            });
        }

        if (c.readsValidationProblems) {
            it('returns the first validation message of a ProblemDetails body', async () => {
                serve(validationProblem({ Field: ['The field is wrong.', 'Second'], Other: ['Other'] }));

                expect(await c.call()).toEqual(failure('The field is wrong.', 400));
            });
        }

        if (c.explainsUnauthorized) {
            it('says the session expired on a 401', async () => {
                serve(new HttpResponse(null, { status: 401 }));

                expect(await c.call()).toEqual(failure(SESSION_EXPIRED, 401));
            });
        }
    });
}
