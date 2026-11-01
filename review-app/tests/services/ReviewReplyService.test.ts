import { describe, expect } from 'vitest';
import { reviewReplyService } from '../../src/services/ReviewReplyService';
import { noContent, ok } from '../support/api';
import { reply } from '../support/builders';
import { describeEndpoint } from './endpoint';

describe('ReviewReplyService', () => {
    const page = { items: [reply()], hasMore: true };

    describeEndpoint({
        name: 'getReplies (a review\'s replies, first page)',
        method: 'get',
        path: 'ReviewReply',
        call: () => reviewReplyService.getReplies(7),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ reviewId: '7', pageSize: '10' }),
        response: ok(page),
        result: { success: true, data: page },
        fallback: 'Something went wrong while getting the replies.',
        network: 'Network error while reaching replies.',
    });

    describeEndpoint({
        name: 'getReplies (children of a reply, after a cursor)',
        method: 'get',
        path: 'ReviewReply',
        call: () => reviewReplyService.getReplies(7, 3, 40, 5),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ reviewId: '7', parentReplyId: '3', afterId: '40', pageSize: '5' }),
        response: ok(page),
        result: { success: true, data: page },
        fallback: 'Something went wrong while getting the replies.',
        network: 'Network error while reaching replies.',
    });

    const context = { reviewID: 7, reply: reply({ id: 34 }), parentReply: reply({ id: 30 }) };

    describeEndpoint({
        name: 'getReplyContext',
        method: 'get',
        path: 'ReviewReply/34',
        call: () => reviewReplyService.getReplyContext(34),
        response: ok(context),
        result: { success: true, data: context },
        fallback: 'Something went wrong while getting the reply.',
        network: 'Network error while reaching the reply.',
        safeErrorBody: true,
    });

    describeEndpoint({
        name: 'createReply',
        method: 'post',
        path: 'ReviewReply',
        call: () => reviewReplyService.createReply({ reviewID: 7, parentReplyID: 3, content: 'Agreed' }),
        request: req => expect(req.body).toEqual({ reviewID: 7, parentReplyID: 3, content: 'Agreed' }),
        response: ok(reply({ id: 50, content: 'Agreed' })),
        result: { success: true, data: expect.objectContaining({ id: 50, content: 'Agreed' }) },
        fallback: 'Something went wrong while posting reply. Please try again.',
        network: 'Network error while reaching replies.',
    });

    describeEndpoint({
        name: 'deleteReply',
        method: 'delete',
        path: 'ReviewReply/50',
        call: () => reviewReplyService.deleteReply(50),
        response: noContent(),
        result: { success: true, data: null },
        fallback: 'Something went wrong while deleting reply. Please try again.',
        network: 'Network error while reaching replies.',
    });
});
