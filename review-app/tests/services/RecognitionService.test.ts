import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { recognitionService } from '../../src/services/RecognitionService';
import { server } from '../support/server';
import { apiUrl, capture, ok } from '../support/api';
import { recognitionAllowance, recognitionResponse, recognizedMedia } from '../support/builders';
import { describeEndpoint } from './endpoint';

const common = {
    safeErrorBody: true,
    explainsUnauthorized: true,
    network: 'Network error. Please check your connection and try again.',
    // The page branches on the status and the API's code
    failureExtras: (status: number) => ({ status }),
};

describe('RecognitionService', () => {
    const allowance = recognitionAllowance();
    const page = { items: [recognizedMedia('people')], total: 12, hasMore: false, nextOffset: 12 };
    const response = recognitionResponse();

    describeEndpoint({
        ...common,
        name: 'getAllowance',
        method: 'get',
        path: 'Recognition/allowance',
        call: () => recognitionService.getAllowance(),
        response: ok(allowance),
        result: { success: true, message: '', data: allowance },
        fallback: 'Something went wrong while loading camera recognition.',
    });

    describeEndpoint({
        ...common,
        name: 'getItems',
        method: 'get',
        path: 'Recognition/rec-1/items',
        call: () => recognitionService.getItems('rec-1', 10),
        request: req => expect(new URL(req.url).searchParams.get('offset')).toBe('10'),
        response: ok(page),
        result: { success: true, message: '', data: page },
        fallback: 'Something went wrong while loading more suggestions.',
    });

    describeEndpoint({
        ...common,
        name: 'recognize',
        method: 'post',
        path: 'Recognition',
        call: () => recognitionService.recognize(new Blob(['jpeg-bytes'], { type: 'image/jpeg' })),
        // The photo goes up as multipart/form-data in the "image" part, with the browser's boundary
        request: req => {
            expect(req.headers.get('content-type')).toMatch(/^multipart\/form-data; boundary=/);
            expect(req.body).toContain('name="image"');
            expect(req.body).toContain('jpeg-bytes');
        },
        response: ok(response),
        result: { success: true, message: '', data: response },
        fallback: 'Something went wrong while recognising the photo.',
    });

    it('passes the API\'s code and reset time along', async () => {
        server.use(capture('post', 'Recognition', HttpResponse.json(
            { error: 'You have used all 20 recognitions for today.', code: 'daily_limit', resetsAt: '2026-11-03T00:00:00Z' },
            { status: 429 })).handler);

        expect(await recognitionService.recognize(new Blob(['x']))).toEqual({
            success: false, status: 429, code: 'daily_limit', resetsAt: '2026-11-03T00:00:00Z',
            message: 'You have used all 20 recognitions for today.',
        });
    });

    it('asks for a given number of items', async () => {
        const endpoint = capture('get', 'Recognition/rec-1/items', ok(page));
        server.use(endpoint.handler);

        await recognitionService.getItems('rec-1', 5, 15);

        const query = new URL(endpoint.last.url).searchParams;
        expect(query.get('offset')).toBe('5');
        expect(query.get('count')).toBe('15');
    });

    it('reports a cancelled upload as aborted, without logging an error', async () => {
        server.use(http.post(apiUrl('Recognition'), async () => {
            await new Promise(resolve => setTimeout(resolve, 1000));
            return HttpResponse.json(response);
        }));
        const controller = new AbortController();

        const result = recognitionService.recognize(new Blob(['x']), controller.signal);
        controller.abort();

        expect(await result).toEqual({ success: false, aborted: true, message: 'Cancelled.' });
    });
});
