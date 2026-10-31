import { describe, expect, it } from 'vitest';
import { HttpResponse } from 'msw';
import { securityService } from '../../src/services/SecurityService';
import { server } from '../support/server';
import { capture, ok } from '../support/api';
import { securityStatus } from '../support/builders';
import { muteConsoleError } from '../support/browser';
import { describeEndpoint } from './endpoint';

const common = {
    safeErrorBody: true,
    readsValidationProblems: true,
    explainsUnauthorized: true,
    network: 'Network error. Please try again.',
};

describe('SecurityService', () => {
    const status = securityStatus({ pendingEmail: 'new@example.com' });

    describeEndpoint({
        ...common,
        name: 'getStatus',
        method: 'get',
        path: 'Security/status',
        call: () => securityService.getStatus(),
        response: ok(status),
        result: { success: true, data: status },
        fallback: 'Something went wrong while loading your security settings.',
        network: 'Network error while loading your security settings.',
    });

    describeEndpoint({
        ...common,
        name: 'verifyEmail',
        method: 'post',
        path: 'Security/verify-email',
        call: () => securityService.verifyEmail('tok-1'),
        request: req => expect(req.body).toEqual({ token: 'tok-1' }),
        response: ok({ message: 'Email verified.' }),
        result: { success: true, message: 'Email verified.', data: { message: 'Email verified.' } },
        fallback: 'Something went wrong while verifying your email.',
    });

    describeEndpoint({
        ...common,
        name: 'resendVerification',
        method: 'post',
        path: 'Security/resend-verification',
        call: () => securityService.resendVerification(),
        request: req => expect(req.body).toBeUndefined(),
        response: ok({ message: 'Sent.' }),
        result: { success: true, message: 'Sent.', data: { message: 'Sent.' } },
        fallback: 'Something went wrong while sending the verification email.',
    });

    describeEndpoint({
        ...common,
        name: 'confirmEmailChange',
        method: 'post',
        path: 'Security/confirm-email-change',
        call: () => securityService.confirmEmailChange('tok-2'),
        request: req => expect(req.body).toEqual({ token: 'tok-2' }),
        response: ok({ message: 'Email changed.' }),
        result: { success: true, message: 'Email changed.', data: { message: 'Email changed.' } },
        fallback: 'Something went wrong while confirming your new email.',
    });

    describeEndpoint({
        ...common,
        name: 'cancelEmailChange',
        method: 'delete',
        path: 'Security/pending-email',
        call: () => securityService.cancelEmailChange(),
        response: new HttpResponse(null, { status: 204 }),
        // No body is fine
        result: { success: true, message: undefined, data: null },
        fallback: 'Something went wrong while cancelling the email change.',
    });

    describe.each([
        [true, 'enable'],
        [false, 'disable'],
    ])('two-factor (%s)', (enable, action) => {
        describeEndpoint({
            ...common,
            name: `requestTwoFactorChange(${action})`,
            method: 'post',
            path: `Security/2fa/${action}/request`,
            call: () => securityService.requestTwoFactorChange(enable, 'hunter22'),
            request: req => expect(req.body).toEqual({ currentPassword: 'hunter22' }),
            response: ok({ message: 'Code sent.' }),
            result: { success: true, message: 'Code sent.', data: { message: 'Code sent.' } },
            fallback: `Something went wrong while trying to ${action} two-factor authentication.`,
        });

        describeEndpoint({
            ...common,
            name: `confirmTwoFactorChange(${action})`,
            method: 'post',
            path: `Security/2fa/${action}/confirm`,
            call: () => securityService.confirmTwoFactorChange(enable, '123456'),
            request: req => expect(req.body).toEqual({ code: '123456' }),
            response: ok({ message: 'Done.' }),
            result: { success: true, message: 'Done.', data: { message: 'Done.' } },
            fallback: `Something went wrong while trying to ${action} two-factor authentication.`,
        });
    });

    it('never logs the request body of a failed request (passwords, codes)', async () => {
        const consoleError = muteConsoleError();
        server.use(capture('post', 'Security/2fa/enable/request', HttpResponse.error()).handler);

        await securityService.requestTwoFactorChange(true, 'secret-password');

        expect(JSON.stringify(consoleError.mock.calls)).not.toContain('secret-password');
    });
});
