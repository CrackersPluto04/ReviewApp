import { describe, expect, it } from 'vitest';
import { HttpResponse } from 'msw';
import { authService } from '../../src/services/AuthService';
import { server } from '../support/server';
import { apiError, capture, networkError, ok } from '../support/api';
import { authUser } from '../support/builders';
import { muteConsoleError } from '../support/browser';
import { describeEndpoint } from './endpoint';

const PASSWORD = 'hunter22';

describe('AuthService', () => {
    describe('register', () => {
        describeEndpoint({
            name: 'register',
            method: 'post',
            path: 'Auth/register',
            // Trimmed before sending; the password is sent as typed
            call: () => authService.register('  alice ', ' alice@example.com ', ` ${PASSWORD}`, ` ${PASSWORD}`),
            request: req => expect(req.body).toEqual({ username: 'alice', email: 'alice@example.com', password: ` ${PASSWORD}` }),
            response: ok({ message: 'Registered.' }),
            result: { success: true, message: 'Registered.' },
            fallback: 'Something went wrong. Please try registering again.',
            network: 'Network error while registering.',
        });

        it.each([
            ['an empty field', ['', 'a@b.cd', PASSWORD, PASSWORD], 'Please fill all the required fields.'],
            ['a whitespace-only username', ['   ', 'a@b.cd', PASSWORD, PASSWORD], 'Please fill all the required fields.'],
            ['a missing confirmation', ['alice', 'a@b.cd', PASSWORD, ''], 'Please fill all the required fields.'],
            ['a 2 character username', ['al', 'a@b.cd', PASSWORD, PASSWORD], 'Username must be between 3 and 20 characters.'],
            ['a 21 character username', ['a'.repeat(21), 'a@b.cd', PASSWORD, PASSWORD], 'Username must be between 3 and 20 characters.'],
            ['an invalid email', ['alice', 'alice@', PASSWORD, PASSWORD], 'Please enter a valid email address.'],
            ['a 7 character password', ['alice', 'a@b.cd', 'x'.repeat(7), 'x'.repeat(7)], 'Password must be between 8 and 20 characters long.'],
            ['a 21 character password', ['alice', 'a@b.cd', 'x'.repeat(21), 'x'.repeat(21)], 'Password must be between 8 and 20 characters long.'],
            ['different passwords', ['alice', 'a@b.cd', PASSWORD, `${PASSWORD}!`], 'Passwords do not match.'],
        ] as const)('rejects %s without a request', async (_, [username, email, password, confirm], message) => {
            const endpoint = capture('post', 'Auth/register');
            server.use(endpoint.handler);

            expect(await authService.register(username, email, password, confirm)).toEqual({ success: false, message });
            expect(endpoint.calls).toHaveLength(0);
        });

        it.each([
            ['3 / 8 characters', 'abc', 'x'.repeat(8)],
            ['20 / 20 characters', 'a'.repeat(20), 'x'.repeat(20)],
        ])('accepts the limits (%s)', async (_, username, password) => {
            const endpoint = capture('post', 'Auth/register', ok({ message: 'ok' }));
            server.use(endpoint.handler);

            await authService.register(username, 'a@b.cd', password, password);

            expect(endpoint.calls).toHaveLength(1);
        });
    });

    describe('login', () => {
        const user = authUser();

        describeEndpoint({
            name: 'login',
            method: 'post',
            path: 'Auth/login',
            call: () => authService.login(' alice@example.com ', PASSWORD),
            request: req => expect(req.body).toEqual({ email: 'alice@example.com', password: PASSWORD }),
            response: ok(user),
            result: { success: true, data: user },
            fallback: 'Something went wrong. Please try logging in again.',
            // The same text, so an unreadable error body still shows the right message
            network: 'Something went wrong. Please try logging in again.',
            safeErrorBody: true,
        });

        it('returns the 2FA challenge as data', async () => {
            server.use(capture('post', 'Auth/login', ok({ twoFactorRequired: true, challengeToken: 'chal' })).handler);

            expect(await authService.login('a@b.cd', PASSWORD)).toEqual({ success: true, data: { twoFactorRequired: true, challengeToken: 'chal' } });
        });

        it.each([
            ['an empty email', ['', PASSWORD], 'Please fill all the required fields.'],
            ['an empty password', ['a@b.cd', ''], 'Please fill all the required fields.'],
            ['an invalid email', ['not-an-email', PASSWORD], 'Please enter a valid email address.'],
            ['a too long password', ['a@b.cd', 'x'.repeat(21)], 'Password can be at most 20 characters long.'],
        ] as const)('rejects %s without a request', async (_, [email, password], message) => {
            const endpoint = capture('post', 'Auth/login');
            server.use(endpoint.handler);

            expect(await authService.login(email, password)).toEqual({ success: false, message });
            expect(endpoint.calls).toHaveLength(0);
        });

        it('lets a short password through (old accounts, the backend decides)', async () => {
            const endpoint = capture('post', 'Auth/login', apiError(401, 'Invalid email or password.'));
            server.use(endpoint.handler);

            expect(await authService.login('a@b.cd', 'x')).toEqual({ success: false, message: 'Invalid email or password.' });
            expect(endpoint.calls).toHaveLength(1);
        });

        it('never logs the password', async () => {
            const consoleError = muteConsoleError();
            server.use(capture('post', 'Auth/login', HttpResponse.json({ title: 'Bad' }, { status: 400 })).handler);

            await authService.login('a@b.cd', 'secret-password');

            expect(JSON.stringify(consoleError.mock.calls)).not.toContain('secret-password');
        });
    });

    describe('two-factor login', () => {
        const user = authUser();

        describeEndpoint({
            name: 'loginTwoFactor',
            method: 'post',
            path: 'Auth/login/2fa',
            call: () => authService.loginTwoFactor('chal', ' 123456 '),
            request: req => expect(req.body).toEqual({ challengeToken: 'chal', code: '123456' }),
            response: ok(user),
            result: { success: true, data: user },
            fallback: 'Something went wrong. Please try again.',
            network: 'Something went wrong. Please try again.',
            safeErrorBody: true,
            readsValidationProblems: true,
        });

        describeEndpoint({
            name: 'resendTwoFactorCode',
            method: 'post',
            path: 'Auth/login/2fa/resend',
            call: () => authService.resendTwoFactorCode('chal'),
            request: req => expect(req.body).toEqual({ challengeToken: 'chal' }),
            response: ok({ message: 'A new code is on its way.' }),
            result: { success: true, message: 'A new code is on its way.' },
            fallback: 'Something went wrong while sending a new code.',
            network: 'Network error while sending a new code.',
            safeErrorBody: true,
            readsValidationProblems: true,
        });
    });

    describe('logout', () => {
        it('posts to logout and never fails', async () => {
            const endpoint = capture('post', 'Auth/logout', ok({}));
            server.use(endpoint.handler);

            await expect(authService.logout()).resolves.toBeUndefined();
            expect(endpoint.last.credentials).toBe('include');

            muteConsoleError();
            server.use(capture('post', 'Auth/logout', networkError()).handler);
            await expect(authService.logout()).resolves.toBeUndefined();
        });
    });

    describeEndpoint({
        name: 'logoutAll',
        method: 'post',
        path: 'Auth/logout-all',
        call: () => authService.logoutAll(),
        response: ok({}),
        result: { success: true },
        fallback: 'Something went wrong while logging out of all devices.',
        network: 'Network error while logging out of all devices.',
        safeErrorBody: true,
    });

    describe('checkAuth', () => {
        it('returns the logged-in user', async () => {
            const user = authUser();
            const endpoint = capture('get', 'Auth/check-auth', ok(user));
            server.use(endpoint.handler);

            expect(await authService.checkAuth()).toEqual({ success: true, data: user });
            expect(endpoint.last.credentials).toBe('include');
        });

        it('fails without a message when logged out or offline', async () => {
            server.use(capture('get', 'Auth/check-auth', apiError(401, 'Unauthorized')).handler);
            expect(await authService.checkAuth()).toEqual({ success: false });

            muteConsoleError();
            server.use(capture('get', 'Auth/check-auth', networkError()).handler);
            expect(await authService.checkAuth()).toEqual({ success: false });
        });
    });
});
