import { describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/preact';
import { LoginPage } from '../../src/pages/LoginPage';
import { useAuth } from '../../src/context/AuthContext';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser } from '../support/builders';
import { currentLocation, currentLocationState, renderWithApp, useFakeTimers } from '../support/render';
import { knownIssue } from '../support/known-issue';

const alice = authUser({ username: 'alice' });

function WhoIsLoggedIn() {
    const { user } = useAuth();
    return <span data-testid="who">{user?.username ?? 'nobody'}</span>;
}

function renderLogin(route: Parameters<typeof renderWithApp>[1]['route'] = '/login') {
    return renderWithApp(<><LoginPage /><WhoIsLoggedIn /></>, { route, user: null });
}

const field = (label: RegExp) => screen.getByLabelText(label);
const EMAIL = /^Email/;
const PASSWORD = /^Password/;

describe('LoginPage', () => {
    describe('login', () => {
        it('logs in and goes home', async () => {
            const login = capture('post', 'Auth/login', ok({ message: 'Welcome back!', user: alice }));
            server.use(login.handler);
            const { user } = renderLogin();

            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22');
            await user.click(screen.getByRole('button', { name: 'Login' }));

            await waitFor(() => expect(currentLocation()).toBe('/home'));
            expect(screen.getByTestId('who')).toHaveTextContent('alice');
            expect(login.last.body).toEqual({ email: 'alice@example.com', password: 'hunter22' });
        });

        it('goes back where the user came from, with its query and state', async () => {
            server.use(capture('post', 'Auth/login', ok({ user: alice })).handler);
            const returnTo = { pathname: '/media/movie/42', search: '?x=1', state: { from: 'card' } };
            const { user } = renderLogin({ pathname: '/login', state: { returnTo } });

            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22{Enter}');

            await waitFor(() => expect(currentLocation()).toBe('/media/movie/42?x=1'));
            expect(currentLocationState()).toEqual({ from: 'card' });
        });

        it('shows the API\'s error and stays on the page', async () => {
            server.use(capture('post', 'Auth/login', apiError(401, 'Invalid email or password.')).handler);
            const { user } = renderLogin();

            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'wrong-pass{Enter}');

            expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.');
            expect(currentLocation()).toBe('/login');
            expect(screen.getByTestId('who')).toHaveTextContent('nobody');
        });

        it('shows the validation error without calling the API', async () => {
            const login = capture('post', 'Auth/login');
            server.use(login.handler);
            const { user } = renderLogin();

            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'x'.repeat(21));
            await user.click(screen.getByRole('button', { name: 'Login' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('Password can be at most 20 characters long.');
            expect(login.calls).toHaveLength(0);
        });

        it('disables the button while logging in', async () => {
            const response = deferred(() => ok({ user: alice }));
            server.use(capture('post', 'Auth/login', () => response.promise).handler);
            const { user } = renderLogin();

            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22{Enter}');

            await waitFor(() => expect(screen.getByRole('button', { name: 'Login' })).toBeDisabled());
            response.release();
            await waitFor(() => expect(currentLocation()).toBe('/home'));
        });
    });

    describe('register', () => {
        async function openRegister() {
            const result = renderLogin();
            await result.user.click(screen.getByRole('button', { name: 'Register here' }));
            return result;
        }

        it('switches to the register form and back', async () => {
            const { user } = await openRegister();

            expect(screen.getByRole('heading', { name: 'Register Account' })).toBeInTheDocument();
            expect(field(/^Username/)).toBeInTheDocument();
            expect(field(/^Confirm Password/)).toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: 'Login here' }));
            expect(screen.getByRole('heading', { name: 'Login to your account' })).toBeInTheDocument();
            expect(screen.queryByLabelText(/^Username/)).not.toBeInTheDocument();
        });

        it('registers and shows the API\'s message', async () => {
            const registerCall = capture('post', 'Auth/register', ok({ message: 'Registered! Check your email.' }));
            server.use(registerCall.handler);
            const { user } = await openRegister();

            await user.type(field(/^Username/), 'alice');
            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22');
            await user.type(field(/^Confirm Password/), 'hunter22');
            await user.click(screen.getByRole('button', { name: 'Register' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('Registered! Check your email.');
            expect(registerCall.last.body).toEqual({ username: 'alice', email: 'alice@example.com', password: 'hunter22' });
            // Not logged in by registering
            expect(currentLocation()).toBe('/login');
        });

        it('flags a mismatching confirmation while typing', async () => {
            const { user } = await openRegister();

            await user.type(field(PASSWORD), 'hunter22');
            await user.type(field(/^Confirm Password/), 'hunter2');

            expect(screen.getByText('Passwords do not match.')).toBeInTheDocument();
            expect(field(/^Confirm Password/)).toHaveAttribute('aria-invalid', 'true');

            await user.type(field(/^Confirm Password/), '2');
            expect(screen.queryByText('Passwords do not match.')).not.toBeInTheDocument();
        });

        it('shows the validation message without calling the API', async () => {
            const registerCall = capture('post', 'Auth/register');
            server.use(registerCall.handler);
            const { user } = await openRegister();

            await user.type(field(/^Username/), 'al');
            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22');
            await user.type(field(/^Confirm Password/), 'hunter22{Enter}');

            expect(await screen.findByRole('alert')).toHaveTextContent('Username must be between 3 and 20 characters.');
            expect(registerCall.calls).toHaveLength(0);
        });

        it('clears the message and confirmation when switching forms', async () => {
            server.use(capture('post', 'Auth/register', apiError(409, 'Username taken.')).handler);
            const { user } = await openRegister();
            await user.type(field(/^Username/), 'alice');
            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22');
            await user.type(field(/^Confirm Password/), 'hunter22{Enter}');
            await screen.findByRole('alert');

            await user.click(screen.getByRole('button', { name: 'Login here' }));
            expect(screen.queryByRole('alert')).not.toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: 'Register here' }));
            expect(field(/^Confirm Password/)).toHaveValue('');
        });

        knownIssue('W6', 'the register button is disabled while registering (no double submit)', async () => {
            const response = deferred(() => ok({ message: 'Registered!' }));
            const registerCall = capture('post', 'Auth/register', () => response.promise);
            server.use(registerCall.handler);
            const { user } = await openRegister();
            await user.type(field(/^Username/), 'alice');
            await user.type(field(EMAIL), 'alice@example.com');
            await user.type(field(PASSWORD), 'hunter22');
            await user.type(field(/^Confirm Password/), 'hunter22');

            await user.click(screen.getByRole('button', { name: 'Register' }));
            await user.click(screen.getByRole('button', { name: 'Register' }));
            response.release();

            await screen.findByRole('alert');
            expect(registerCall.calls).toHaveLength(1);
        });
    });

    describe('two-factor step', () => {
        const challenge = ok({ twoFactorRequired: true, challengeToken: 'chal-1', message: 'We sent a code to your email.' });

        async function reachCodeStep() {
            server.use(capture('post', 'Auth/login', challenge).handler);
            const result = renderLogin();
            await result.user.type(field(EMAIL), 'alice@example.com');
            await result.user.type(field(PASSWORD), 'hunter22{Enter}');
            await screen.findByRole('heading', { name: 'Two-factor authentication' });
            return result;
        }

        it('asks for the emailed code instead of logging in', async () => {
            await reachCodeStep();

            expect(screen.getByRole('alert')).toHaveTextContent('We sent a code to your email.');
            expect(screen.getByTestId('who')).toHaveTextContent('nobody');
            expect(screen.getByRole('button', { name: 'Verify and log in' })).toBeDisabled();
        });

        it('accepts only digits, at most 6', async () => {
            const { user } = await reachCodeStep();

            await user.type(field(/^Code/), '12a-34 5678');

            expect(field(/^Code/)).toHaveValue('123456');
            expect(screen.getByRole('button', { name: 'Verify and log in' })).toBeEnabled();
        });

        it('logs in with the code', async () => {
            const verify = capture('post', 'Auth/login/2fa', ok({ user: alice }));
            server.use(verify.handler);
            const { user } = await reachCodeStep();

            await user.type(field(/^Code/), '123456{Enter}');

            await waitFor(() => expect(currentLocation()).toBe('/home'));
            expect(screen.getByTestId('who')).toHaveTextContent('alice');
            expect(verify.last.body).toEqual({ challengeToken: 'chal-1', code: '123456' });
        });

        it('shows a wrong code\'s error and clears the field', async () => {
            server.use(capture('post', 'Auth/login/2fa', apiError(400, 'The code is wrong or has expired.')).handler);
            const { user } = await reachCodeStep();

            await user.type(field(/^Code/), '000000{Enter}');

            expect(await screen.findByText('The code is wrong or has expired.')).toBeInTheDocument();
            expect(field(/^Code/)).toHaveValue('');
            expect(currentLocation()).toBe('/login');
        });

        it('lets the user resend the code after the 60 s cooldown', async () => {
            useFakeTimers();
            const resend = capture('post', 'Auth/login/2fa/resend', ok({ message: 'A new code is on its way.' }));
            server.use(resend.handler);
            const { user } = await reachCodeStep();

            expect(screen.getByRole('button', { name: /Resend code \(60s\)/ })).toBeDisabled();
            // Outside act: inside it Preact holds every effect until act ends, so the countdown would stop after one tick
            await vi.advanceTimersByTimeAsync(30_000);
            expect(screen.getByRole('button', { name: /Resend code \(\d+s\)/ })).toBeDisabled();
            // Each 1 s tick starts after the previous render's effect (up to 35 ms later)
            await vi.advanceTimersByTimeAsync(40_000);

            await user.click(await screen.findByRole('button', { name: 'Resend code' }));

            expect(await screen.findByText('A new code is on its way.')).toBeInTheDocument();
            expect(resend.last.body).toEqual({ challengeToken: 'chal-1' });
            // The cooldown starts again
            expect(screen.getByRole('button', { name: /Resend code \(\d+s\)/ })).toBeDisabled();
        });

        it('keeps the button enabled when the resend fails', async () => {
            useFakeTimers();
            server.use(capture('post', 'Auth/login/2fa/resend', apiError(429, 'Too many requests.')).handler);
            const { user } = await reachCodeStep();
            await vi.advanceTimersByTimeAsync(70_000);

            await user.click(await screen.findByRole('button', { name: 'Resend code' }));

            expect(await screen.findByText('Too many requests.')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Resend code' })).toBeEnabled();
        });

        it('goes back to the login form without the password', async () => {
            const { user } = await reachCodeStep();

            await user.click(screen.getByRole('button', { name: 'Back to login' }));

            expect(screen.getByRole('heading', { name: 'Login to your account' })).toBeInTheDocument();
            expect(field(EMAIL)).toHaveValue('alice@example.com');
            expect(field(PASSWORD)).toHaveValue('');
            expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
    });
});
