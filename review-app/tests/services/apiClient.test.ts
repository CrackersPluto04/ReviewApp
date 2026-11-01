import { describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
    apiFetch, setMutationHandler, setPremiumRequiredHandler, setUnauthorizedHandler, setUnlockHandler,
} from '../../src/services/apiClient';
import { server } from '../support/server';
import { apiUrl, networkError, paymentRequired, unauthorized } from '../support/api';
import { unlocked } from '../support/builders';
import { muteConsoleError } from '../support/browser';

const URL = apiUrl('Thing');

function respondWith(response: () => Response) {
    server.use(http.all(URL, response));
}

describe('apiFetch', () => {
    it('passes the request through and returns the response unchanged', async () => {
        server.use(http.post(URL, async ({ request }) => HttpResponse.json({ echo: await request.json() }, { status: 201 })));

        const response = await apiFetch(URL, { method: 'POST', body: JSON.stringify({ a: 1 }) });

        expect(response.status).toBe(201);
        expect(await response.json()).toEqual({ echo: { a: 1 } });
    });

    describe('401', () => {
        it('reports a lost session to the unauthorized handler and still returns the response', async () => {
            const onUnauthorized = vi.fn();
            setUnauthorizedHandler(onUnauthorized);
            respondWith(unauthorized);

            const response = await apiFetch(URL);

            expect(onUnauthorized).toHaveBeenCalledTimes(1);
            expect(response.status).toBe(401);
        });

        it('does not call the handler for other errors', async () => {
            const onUnauthorized = vi.fn();
            setUnauthorizedHandler(onUnauthorized);
            respondWith(() => HttpResponse.json({ error: 'Forbidden' }, { status: 403 }));

            await apiFetch(URL);

            expect(onUnauthorized).not.toHaveBeenCalled();
        });

        it('works without a registered handler and after unregistering', async () => {
            const onUnauthorized = vi.fn();
            setUnauthorizedHandler(onUnauthorized);
            setUnauthorizedHandler(null);
            respondWith(unauthorized);

            await expect(apiFetch(URL)).resolves.toHaveProperty('status', 401);
            expect(onUnauthorized).not.toHaveBeenCalled();
        });
    });

    describe('402', () => {
        it('reports a Premium feature to the premium required handler and still returns the response', async () => {
            const onPremiumRequired = vi.fn();
            setPremiumRequiredHandler(onPremiumRequired);
            respondWith(() => paymentRequired());

            const response = await apiFetch(URL);

            expect(onPremiumRequired).toHaveBeenCalledTimes(1);
            expect(response.status).toBe(402);
            expect(await response.json()).toMatchObject({ code: 'premium_required' });
        });
    });

    describe('X-Unlocked-Achievements', () => {
        it('passes the unlocked tiers to the unlock handler', async () => {
            const onUnlock = vi.fn();
            setUnlockHandler(onUnlock);
            const tiers = [unlocked({ tier: 'Bronze' }), unlocked({ tier: 'Silver' })];
            respondWith(() => HttpResponse.json({}, { headers: { 'X-Unlocked-Achievements': JSON.stringify(tiers) } }));

            await apiFetch(URL, { method: 'POST' });

            expect(onUnlock).toHaveBeenCalledWith(tiers);
        });

        it('reads the header on error responses too', async () => {
            const onUnlock = vi.fn();
            setUnlockHandler(onUnlock);
            respondWith(() => HttpResponse.json({ error: 'x' }, { status: 400, headers: { 'X-Unlocked-Achievements': JSON.stringify([unlocked()]) } }));

            await apiFetch(URL);

            expect(onUnlock).toHaveBeenCalledTimes(1);
        });

        it.each([
            ['an empty list', '[]'],
            ['not a list', '{"groupCode":"X"}'],
        ])('ignores %s', async (_, value) => {
            const onUnlock = vi.fn();
            setUnlockHandler(onUnlock);
            respondWith(() => HttpResponse.json({}, { headers: { 'X-Unlocked-Achievements': value } }));

            await apiFetch(URL);

            expect(onUnlock).not.toHaveBeenCalled();
        });

        it('never breaks the request on a malformed header', async () => {
            const consoleError = muteConsoleError();
            const onUnlock = vi.fn();
            setUnlockHandler(onUnlock);
            respondWith(() => HttpResponse.json({ ok: true }, { headers: { 'X-Unlocked-Achievements': 'not json' } }));

            const response = await apiFetch(URL);

            expect(await response.json()).toEqual({ ok: true });
            expect(onUnlock).not.toHaveBeenCalled();
            expect(consoleError).toHaveBeenCalled();
        });
    });

    describe('mutation handler', () => {
        it.each(['POST', 'PUT', 'PATCH', 'DELETE', 'post'])('is called after a successful %s', async method => {
            const onMutation = vi.fn();
            setMutationHandler(onMutation);
            respondWith(() => HttpResponse.json({}));

            await apiFetch(URL, { method });

            expect(onMutation).toHaveBeenCalledTimes(1);
        });

        it('is not called for GET (explicit or default)', async () => {
            const onMutation = vi.fn();
            setMutationHandler(onMutation);
            respondWith(() => HttpResponse.json({}));

            await apiFetch(URL);
            await apiFetch(URL, { method: 'GET' });
            await apiFetch(URL, { credentials: 'include' });

            expect(onMutation).not.toHaveBeenCalled();
        });

        it('is not called when the change failed', async () => {
            const onMutation = vi.fn();
            setMutationHandler(onMutation);
            respondWith(() => HttpResponse.json({ error: 'Nope' }, { status: 400 }));

            await apiFetch(URL, { method: 'POST' });

            expect(onMutation).not.toHaveBeenCalled();
        });
    });

    it('lets a network failure reject (the services catch it) without calling any handler', async () => {
        const handlers = [vi.fn(), vi.fn(), vi.fn(), vi.fn()];
        setUnauthorizedHandler(handlers[0]);
        setPremiumRequiredHandler(handlers[1]);
        setUnlockHandler(handlers[2]);
        setMutationHandler(handlers[3]);
        respondWith(networkError);

        await expect(apiFetch(URL, { method: 'POST' })).rejects.toThrow();
        handlers.forEach(h => expect(h).not.toHaveBeenCalled());
    });
});
