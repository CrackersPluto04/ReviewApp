import { describe, expect, it } from 'vitest';
import { subscriptionService } from '../../src/services/SubscriptionService';
import { server } from '../support/server';
import { capture, ok, paymentRequired } from '../support/api';
import { activeSubscription, plan } from '../support/builders';
import { describeEndpoint } from './endpoint';

const common = {
    safeErrorBody: true,
    readsValidationProblems: true,
    explainsUnauthorized: true,
    network: 'Network error. Please try again.',
    // The callers branch on the status (e.g. 402 = not Premium)
    failureExtras: (status: number) => ({ status }),
};

describe('SubscriptionService', () => {
    const thePlan = plan();
    const status = activeSubscription();

    describeEndpoint({
        ...common,
        name: 'getPlan',
        method: 'get',
        path: 'Subscription/plan',
        call: () => subscriptionService.getPlan(),
        response: ok(thePlan),
        result: { success: true, message: undefined, data: thePlan },
        fallback: 'Something went wrong while loading the plans.',
    });

    describeEndpoint({
        ...common,
        name: 'getStatus',
        method: 'get',
        path: 'Subscription/status',
        call: () => subscriptionService.getStatus(),
        response: ok(status),
        result: { success: true, message: undefined, data: status },
        fallback: 'Something went wrong while loading your subscription.',
    });

    describeEndpoint({
        ...common,
        name: 'startCheckout',
        method: 'post',
        path: 'Subscription/checkout',
        call: () => subscriptionService.startCheckout(),
        request: req => expect(req.body).toEqual({ platform: 'web' }),
        response: ok({ url: 'https://pay.example/checkout' }),
        result: { success: true, message: undefined, data: { url: 'https://pay.example/checkout' } },
        fallback: 'Something went wrong while starting the checkout.',
    });

    describeEndpoint({
        ...common,
        name: 'openPortal',
        method: 'post',
        path: 'Subscription/portal',
        call: () => subscriptionService.openPortal(),
        request: req => expect(req.body).toEqual({ platform: 'web' }),
        response: ok({ url: 'https://pay.example/portal' }),
        result: { success: true, message: undefined, data: { url: 'https://pay.example/portal' } },
        fallback: 'Something went wrong while opening the payment settings.',
    });

    describe.each([
        ['cancel', () => subscriptionService.cancel(), 'Something went wrong while cancelling your subscription.'],
        ['resume', () => subscriptionService.resume(), 'Something went wrong while resuming your subscription.'],
        ['sync', () => subscriptionService.sync(), 'Something went wrong while checking your subscription.'],
    ] as const)('%s', (action, call, fallback) => {
        describeEndpoint({
            ...common,
            name: action,
            method: 'post',
            path: `Subscription/${action}`,
            call,
            request: req => expect(req.body).toBeUndefined(),
            response: ok({ message: 'Done.', ...status }),
            result: { success: true, message: 'Done.', data: { message: 'Done.', ...status } },
            fallback,
        });
    });

    describeEndpoint({
        ...common,
        name: 'tryEntitlementPreview',
        method: 'get',
        path: 'Subscription/entitlement-preview',
        call: () => subscriptionService.tryEntitlementPreview(),
        response: ok({ message: 'Welcome to the preview.' }),
        result: { success: true, message: 'Welcome to the preview.', data: { message: 'Welcome to the preview.' } },
        fallback: 'Something went wrong while opening the preview.',
    });

    it('reports a 402 with its status and the API\'s message', async () => {
        server.use(capture('get', 'Subscription/entitlement-preview', paymentRequired()).handler);

        expect(await subscriptionService.tryEntitlementPreview()).toEqual({
            success: false, status: 402, message: 'This is a Premium feature.',
        });
    });
});
