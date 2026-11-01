import { http, HttpResponse } from 'msw';
import { server } from './server';
import { apiUrl, capture } from './api';
import { paged } from './builders';

/**
 * Handlers for the requests that many components make on their own (every MediaCard loads its score,
 * every ReviewCard can load replies), so the tests about something else don't have to spell them out.
 */

// Average score of every media card; `byExternalId` overrides it per media
export function serveStats(stats = { averageScore: 0, reviewCount: 0 }, byExternalId: Record<string, { averageScore: number; reviewCount: number }> = {}) {
    const endpoint = capture('get', 'Review/stats/average-score', req =>
        HttpResponse.json(byExternalId[req.url.searchParams.get('externalApiId') ?? ''] ?? stats));
    server.use(endpoint.handler);
    return endpoint;
}

// A review's (or reply's) replies: empty unless given
export function serveReplies(items: unknown[] = [], hasMore = false) {
    const endpoint = capture('get', 'ReviewReply', () => HttpResponse.json({ items, hasMore }));
    server.use(endpoint.handler);
    return endpoint;
}

export function serveMediaReviews(items: unknown[] = [], overrides: Record<string, any> = {}) {
    const endpoint = capture('get', 'Review/media/:mediaType/:externalApiId', () => HttpResponse.json(paged(items, overrides)));
    server.use(endpoint.handler);
    return endpoint;
}

// The logged-in user's collections, for the "add to collection" menu
export function serveUserCollections(collections: unknown[]) {
    const endpoint = capture('get', 'User/:username/collections', () => HttpResponse.json(collections));
    server.use(endpoint.handler);
    return endpoint;
}

export function serveSearch(what: 'all' | 'movie' | 'series' | 'music', respond: (req: Request) => Response) {
    server.use(http.get(apiUrl(`Media/search/${what}`), ({ request }) => respond(request)));
}
