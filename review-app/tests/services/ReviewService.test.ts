import { describe, expect, it } from 'vitest';
import { HttpResponse } from 'msw';
import { reviewService } from '../../src/services/ReviewService';
import { server } from '../support/server';
import { apiError, capture, htmlError, networkError, noContent, ok } from '../support/api';
import { movie, review } from '../support/builders';
import { muteConsoleError } from '../support/browser';
import { describeEndpoint } from './endpoint';

describe('ReviewService', () => {
    const page = { items: [review()], totalPages: 3, totalCount: 25 };

    describeEndpoint({
        name: 'getMediaReviews',
        method: 'get',
        path: 'Review/media/1/tmdb-42',
        call: () => reviewService.getMediaReviews({ mediaType: 1, externalApiId: 'tmdb-42', page: 2, sortBy: 'newest', minScore: 3, maxScore: 9, hasWrittenText: true }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({
            page: '2', sortBy: 'newest', minScore: '3', maxScore: '9', hasWrittenText: 'true',
        }),
        // Anonymous endpoint: the backend lists only public reviews, for everyone
        credentials: undefined,
        response: ok(page),
        result: { success: true, data: page },
        fallback: 'Something went wrong while getting the reviews.',
        network: 'Network error while reaching reviews.',
    });

    describe('getReview', () => {
        const single = review({ id: 12 });

        it('returns the review', async () => {
            const endpoint = capture('get', 'Review/12', ok(single));
            server.use(endpoint.handler);

            expect(await reviewService.getReview(12)).toEqual({ success: true, data: single });
            // The author sees their own private review
            expect(endpoint.last.credentials).toBe('include');
        });

        it('marks a missing or hidden review as not found', async () => {
            server.use(capture('get', 'Review/12', apiError(404, 'Review not found.')).handler);

            expect(await reviewService.getReview(12)).toEqual({ success: false, notFound: true, message: 'Review not found.' });
        });

        it('separates other failures from not found', async () => {
            muteConsoleError();
            server.use(capture('get', 'Review/12', htmlError(500)).handler);
            expect(await reviewService.getReview(12)).toEqual({
                success: false, notFound: false, message: 'Something went wrong while getting the review.',
            });

            server.use(capture('get', 'Review/12', networkError()).handler);
            expect(await reviewService.getReview(12)).toEqual({
                success: false, notFound: false, message: 'Network error while reaching the review.',
            });
        });
    });

    describeEndpoint({
        name: 'getAverageScore',
        method: 'get',
        path: 'Review/stats/average-score',
        call: () => reviewService.getAverageScore('tmdb-42', 0),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ externalApiId: 'tmdb-42', mediaType: '0' }),
        credentials: undefined,
        response: ok({ averageScore: 7.5, reviewCount: 4 }),
        result: { success: true, data: { averageScore: 7.5, reviewCount: 4 } },
        fallback: 'Something went wrong while getting the average score.',
        network: 'Network error while reaching reviews.',
    });

    describeEndpoint({
        name: 'checkIfUserReviewedMedia',
        method: 'get',
        path: 'Review/check',
        call: () => reviewService.checkIfUserReviewedMedia('tmdb-42', 0),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ externalApiId: 'tmdb-42', mediaType: '0' }),
        response: ok({ hasReviewed: true, review: { id: 3 } }),
        result: { success: true, data: { hasReviewed: true, review: { id: 3 } } },
        fallback: 'Something went wrong while checking review status.',
        network: 'Network error while reaching reviews.',
    });

    const body = {
        mediaDto: movie({ externalApiID: 'tmdb-42' }),
        reviewDto: { score: 8, reviewText: 'Great', pros: 'Cast', cons: 'Length', visibilityLevel: 1 },
    };

    describeEndpoint({
        name: 'createReview',
        method: 'post',
        path: 'Review',
        call: () => reviewService.createReview(body),
        request: req => expect(req.body).toEqual(body),
        response: ok({ id: 3 }),
        result: { success: true, data: { id: 3 } },
        fallback: 'Something went wrong while creating review. Please try again.',
        network: 'Network error while reaching reviews.',
    });

    describeEndpoint({
        name: 'editReview',
        method: 'put',
        path: 'Review',
        call: () => reviewService.editReview(body),
        request: req => expect(req.body).toEqual(body),
        response: ok({ id: 3 }),
        result: { success: true, data: { id: 3 } },
        fallback: 'Something went wrong while editing review. Please try again.',
        network: 'Network error while reaching reviews.',
    });

    describeEndpoint({
        name: 'deleteReview',
        method: 'delete',
        path: 'Review/3',
        call: () => reviewService.deleteReview(3),
        response: noContent(),
        result: { success: true, data: null },
        fallback: 'Something went wrong while deleting review. Please try again.',
        network: 'Network error while reaching reviews.',
    });

    it('sends a review with text and score exactly as given', async () => {
        const endpoint = capture('post', 'Review', HttpResponse.json({ id: 1 }));
        server.use(endpoint.handler);

        await reviewService.createReview({ ...body, reviewDto: { score: 0.5, visibilityLevel: 0 } });

        expect(endpoint.last.body.reviewDto).toEqual({ score: 0.5, visibilityLevel: 0 });
    });
});
