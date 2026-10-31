import { describe, expect, it } from 'vitest';
import { HttpResponse } from 'msw';
import { notificationService } from '../../src/services/NotificationService';
import { server } from '../support/server';
import { apiError, capture, networkError, noContent, ok } from '../support/api';
import { notification, notificationPage } from '../support/builders';
import { muteConsoleError } from '../support/browser';
import { describeEndpoint } from './endpoint';

const common = {
    safeErrorBody: true,
    explainsUnauthorized: true,
    network: 'Network error while reaching notifications.',
};

describe('NotificationService', () => {
    const page = notificationPage([notification()]);

    describeEndpoint({
        ...common,
        name: 'getNotifications (first page)',
        method: 'get',
        path: 'Notification',
        call: () => notificationService.getNotifications({ status: 'unseen', sort: 'newest' }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ status: 'unseen', sort: 'newest' }),
        response: ok(page),
        result: { success: true, data: page },
        fallback: 'Something went wrong while getting your notifications.',
    });

    describeEndpoint({
        ...common,
        name: 'getNotifications (next page)',
        method: 'get',
        path: 'Notification',
        call: () => notificationService.getNotifications({ status: 'all', sort: 'oldest', afterId: 0, pageSize: 20 }),
        // afterId 0 is still sent
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ status: 'all', sort: 'oldest', afterId: '0', pageSize: '20' }),
        response: ok(page),
        result: { success: true, data: page },
        fallback: 'Something went wrong while getting your notifications.',
    });

    describe('getUnseenCount', () => {
        it('returns the count', async () => {
            const endpoint = capture('get', 'Notification/unseen-count', ok({ count: 4 }));
            server.use(endpoint.handler);

            expect(await notificationService.getUnseenCount()).toEqual({ success: true, data: 4 });
            expect(endpoint.last.credentials).toBe('include');
        });

        it('fails quietly (no message) on errors and network failures', async () => {
            muteConsoleError();
            server.use(capture('get', 'Notification/unseen-count', apiError(500, 'Down')).handler);
            expect(await notificationService.getUnseenCount()).toEqual({ success: false });

            server.use(capture('get', 'Notification/unseen-count', networkError()).handler);
            expect(await notificationService.getUnseenCount()).toEqual({ success: false });
        });
    });

    describeEndpoint({
        ...common,
        name: 'setSeen',
        method: 'patch',
        path: 'Notification/3/seen',
        call: () => notificationService.setSeen(3, false),
        request: req => expect(req.body).toEqual({ isSeen: false }),
        response: noContent(),
        result: { success: true },
        fallback: 'Something went wrong while updating the notification.',
    });

    describeEndpoint({
        ...common,
        name: 'markAllSeen',
        method: 'post',
        path: 'Notification/seen-all',
        call: () => notificationService.markAllSeen(),
        request: req => expect(req.body).toBeUndefined(),
        response: noContent(),
        result: { success: true },
        fallback: 'Something went wrong while marking your notifications as seen.',
    });

    describeEndpoint({
        ...common,
        name: 'deleteNotification',
        method: 'delete',
        path: 'Notification/3',
        call: () => notificationService.deleteNotification(3),
        response: noContent(),
        result: { success: true },
        fallback: 'Something went wrong while deleting the notification.',
    });

    describeEndpoint({
        ...common,
        name: 'deleteSeen',
        method: 'delete',
        path: 'Notification/seen',
        call: () => notificationService.deleteSeen(),
        response: noContent(),
        result: { success: true },
        fallback: 'Something went wrong while clearing your seen notifications.',
    });

    describe('already gone', () => {
        it('treats a 404 on delete as done (deleted in another tab)', async () => {
            server.use(capture('delete', 'Notification/3', HttpResponse.json({ error: 'Not found' }, { status: 404 })).handler);

            expect(await notificationService.deleteNotification(3)).toEqual({ success: true });
        });

        it('still reports a 404 on other changes', async () => {
            server.use(capture('patch', 'Notification/3/seen', HttpResponse.json({ error: 'Notification not found.' }, { status: 404 })).handler);

            expect(await notificationService.setSeen(3, true)).toEqual({ success: false, message: 'Notification not found.' });
        });
    });
});
