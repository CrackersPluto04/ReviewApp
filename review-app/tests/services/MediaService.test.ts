import { beforeEach, describe, expect, it } from 'vitest';
import { mediaService } from '../../src/services/MediaService';
import { server } from '../support/server';
import { apiError, capture, ok } from '../support/api';
import { album, movie } from '../support/builders';
import { knownIssue } from '../support/known-issue';
import { describeEndpoint } from './endpoint';

describe('MediaService', () => {
    const details = movie({ externalApiID: 'tmdb-42', title: 'Arrival' });

    // The media endpoints are anonymous and the same for everyone
    const anonymous = { credentials: undefined };

    describeEndpoint({
        ...anonymous,
        name: 'getMediaDetails',
        method: 'get',
        path: 'Media/media/movie/tmdb-42',
        call: () => mediaService.getMediaDetails('movie', 'tmdb-42'),
        response: ok(details),
        result: { success: true, data: details },
        fallback: 'Failed to fetch media details',
        network: 'Network error while fetching media details.',
    });

    const results = { items: [details], totalPages: 2 };

    describe.each([
        ['searchAll', 'all', () => mediaService.searchAll('a&b'), '1'],
        ['searchMovies', 'movie', () => mediaService.searchMovies('a&b', 3), '3'],
        ['searchSeries', 'series', () => mediaService.searchSeries('a&b'), '1'],
    ] as const)('%s', (name, what, call, page) => {
        describeEndpoint({
            ...anonymous,
            name,
            method: 'get',
            path: `Media/search/${what}`,
            call,
            // The query is escaped
            request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ query: 'a&b', page }),
            response: ok(results),
            result: { success: true, data: results },
            fallback: 'Search failed',
            network: 'Network error while searching.',
        });
    });

    describeEndpoint({
        ...anonymous,
        name: 'discoverMovies',
        method: 'get',
        path: 'Media/discover/movie',
        call: () => mediaService.discoverMovies({ page: 2, sortBy: 'popularity.desc', year: '1999', withGenres: '28,12', minRuntime: '60', maxRuntime: '120' }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({
            page: '2', sortBy: 'popularity.desc', year: '1999', withGenres: '28,12', minRuntime: '60', maxRuntime: '120',
        }),
        response: ok(results),
        result: { success: true, data: results },
        fallback: 'Discovery failed',
        network: 'Network error while discovering.',
    });

    describeEndpoint({
        ...anonymous,
        name: 'discoverSeries (optional filters left out)',
        method: 'get',
        path: 'Media/discover/series',
        call: () => mediaService.discoverSeries({ page: 1, sortBy: 'vote_average.desc', year: '', withGenres: '' }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ page: '1', sortBy: 'vote_average.desc' }),
        response: ok(results),
        result: { success: true, data: results },
        fallback: 'Discovery failed',
        network: 'Network error while discovering.',
    });

    describeEndpoint({
        ...anonymous,
        name: 'discoverMusic',
        method: 'get',
        path: 'Media/discover/music',
        call: () => mediaService.discoverMusic({ page: 1, genre: 'jazz', year: '1960', market: 'HU' }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ page: '1', genre: 'jazz', year: '1960', market: 'HU' }),
        response: ok(results),
        result: { success: true, data: results },
        fallback: 'Music Discovery failed',
        network: 'Network error while discovering music.',
    });

    describe('searchMusic', () => {
        // The music search endpoint gives 10 tracks a page; the service collects 20 unique ones per UI page
        let query = 0;
        let term: string;
        beforeEach(() => { term = `term-${++query}`; });

        const tracks = (from: number, count: number) =>
            Array.from({ length: count }, (_, i) => album({ externalApiID: `sp-${from + i}`, title: `Track ${from + i}` }));

        function serveMusic(pages: Record<number, ReturnType<typeof album>[] | 'error'>) {
            const endpoint = capture('get', 'Media/search/music', req => {
                const items = pages[Number(req.url.searchParams.get('page'))] ?? [];
                return items === 'error' ? apiError(500, 'Spotify is down') : ok({ items });
            });
            server.use(endpoint.handler);
            return endpoint;
        }

        it('fetches backend pages until it has 20 unique tracks', async () => {
            const endpoint = serveMusic({ 1: tracks(1, 10), 2: tracks(11, 10), 3: tracks(21, 10) });

            const result = await mediaService.searchMusic(term);

            expect(endpoint.calls.map(c => c.url.searchParams.get('page'))).toEqual(['1', '2']);
            expect(endpoint.last.url.searchParams.get('query')).toBe(term);
            expect(result.success).toBe(true);
            expect(result.data!.items.map((t: any) => t.externalApiID)).toEqual(tracks(1, 20).map(t => t.externalApiID));
            // More may come: one extra page keeps "next" enabled
            expect(result.data!.totalPages).toBe(2);
            expect(result.data!.totalCount).toBe(20);
        });

        it('drops duplicates across backend pages', async () => {
            const endpoint = serveMusic({ 1: tracks(1, 10), 2: [...tracks(5, 5), ...tracks(11, 5)], 3: tracks(16, 10) });

            const result = await mediaService.searchMusic(term);

            expect(endpoint.calls).toHaveLength(3);
            const ids = result.data!.items.map((t: any) => t.externalApiID);
            expect(new Set(ids).size).toBe(20);
            expect(ids.slice(0, 12)).toEqual([...tracks(1, 10), ...tracks(11, 2)].map(t => t.externalApiID));
        });

        it('serves a later page from the buffer, fetching only what is missing', async () => {
            const endpoint = serveMusic({ 1: tracks(1, 10), 2: tracks(11, 10), 3: tracks(21, 10), 4: tracks(31, 10) });

            await mediaService.searchMusic(term, 1);
            const second = await mediaService.searchMusic(term, 2);
            const firstAgain = await mediaService.searchMusic(term, 1);

            expect(endpoint.calls.map(c => c.url.searchParams.get('page'))).toEqual(['1', '2', '3', '4']);
            expect(second.data!.items[0].externalApiID).toBe('sp-21');
            expect(firstAgain.data!.items[0].externalApiID).toBe('sp-1');
        });

        it('stops at the end of the results and reports the real page count', async () => {
            serveMusic({ 1: tracks(1, 10), 2: tracks(11, 5) });

            const result = await mediaService.searchMusic(term);

            expect(result.data!.items).toHaveLength(15);
            expect(result.data!.totalPages).toBe(1);
        });

        it('starts over for a new query', async () => {
            const endpoint = serveMusic({ 1: tracks(1, 10) });

            await mediaService.searchMusic(term);
            await mediaService.searchMusic(`${term}-other`);

            expect(endpoint.calls.map(c => c.url.searchParams.get('query'))).toEqual([term, term, `${term}-other`, `${term}-other`]);
        });

        it('has no results for an empty search', async () => {
            serveMusic({});

            expect(await mediaService.searchMusic(term)).toEqual({ success: true, data: { items: [], totalCount: 0, totalPages: 0 } });
        });

        knownIssue('W4', 'a failed backend page is reported as a failure, not as a shorter result', async () => {
            serveMusic({ 1: tracks(1, 10), 2: 'error' });

            const result = await mediaService.searchMusic(term);

            expect(result.success).toBe(false);
        });

        it('fetches a failed backend page again on the next attempt', async () => {
            let fail = true;
            server.use(capture('get', 'Media/search/music', req => {
                const page = Number(req.url.searchParams.get('page'));
                if (page === 2 && fail) { fail = false; return apiError(500, 'Spotify is down'); }
                return ok({ items: page <= 3 ? tracks(page * 10 - 9, 10) : [] });
            }).handler);

            await mediaService.searchMusic(term);
            const retry = await mediaService.searchMusic(term);

            expect(retry.data!.items).toHaveLength(20);
        });
    });
});
