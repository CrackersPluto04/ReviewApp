import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { EmailLinkPage } from '../../src/pages/EmailLinkPage';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';

function renderLink(mode: 'verify' | 'change', search: string, user: ReturnType<typeof authUser> | null) {
    const path = mode === 'verify' ? '/verify-email' : '/confirm-email-change';
    return renderWithApp(<EmailLinkPage mode={mode} />, { route: `${path}${search}`, user });
}

describe('EmailLinkPage', () => {
    describe('verify', () => {
        it('posts the token once, shows the result and reloads the user', async () => {
            const verify = capture('post', 'Security/verify-email', ok({ message: 'Your email is verified.' }));
            let checks = 0;
            server.use(verify.handler, capture('get', 'Auth/check-auth', () => { checks++; return ok(authUser()); }).handler);
            renderWithApp(<EmailLinkPage mode="verify" />, { route: '/verify-email?token=abc%2B1' });

            expect(screen.getByRole('heading', { name: 'Email verification' })).toBeInTheDocument();
            expect(await screen.findByRole('alert')).toHaveTextContent('Your email is verified.');
            expect(verify.calls).toHaveLength(1);
            expect(verify.last.body).toEqual({ token: 'abc+1' });
            await waitFor(() => expect(checks).toBe(2));
        });

        it('waits while the token is checked', async () => {
            const response = deferred(() => ok({ message: 'Verified.' }));
            server.use(capture('post', 'Security/verify-email', () => response.promise).handler);
            renderLink('verify', '?token=t', null);

            expect(screen.getByText('Just a moment...')).toBeInTheDocument();
            expect(screen.queryByRole('button')).not.toBeInTheDocument();

            response.release();
            expect(await screen.findByRole('alert')).toHaveTextContent('Verified.');
        });

        it('offers to log in to a visitor and home to a logged-in user', async () => {
            server.use(capture('post', 'Security/verify-email', ok({ message: 'Verified.' })).handler);
            const visitor = renderLink('verify', '?token=t', null);
            await screen.findByRole('alert');
            await visitor.user.click(screen.getByRole('button', { name: 'Log in' }));
            expect(currentLocation()).toBe('/login');
            visitor.unmount();

            const member = renderLink('verify', '?token=t', authUser());
            await screen.findByRole('alert');
            await member.user.click(await screen.findByRole('button', { name: 'Go to the home page' }));
            expect(currentLocation()).toBe('/home');
        });

        it('explains an incomplete link without calling the API', async () => {
            const verify = capture('post', 'Security/verify-email');
            server.use(verify.handler);
            renderLink('verify', '', null);

            expect(await screen.findByRole('alert')).toHaveTextContent('This link is incomplete. Please open the link from the email again.');
            expect(verify.calls).toHaveLength(0);
        });

        it('lets a logged-in user ask for a new link after a failure', async () => {
            server.use(
                capture('post', 'Security/verify-email', apiError(400, 'This link has expired.')).handler,
                capture('post', 'Security/resend-verification', ok({ message: 'A new email is on its way.' })).handler,
            );
            const { user } = renderLink('verify', '?token=old', authUser({ isEmailConfirmed: false }));

            expect(await screen.findByText('This link has expired.')).toBeInTheDocument();
            await user.click(await screen.findByRole('button', { name: 'Send a new verification email' }));

            expect(await screen.findByText('A new email is on its way.')).toBeInTheDocument();
        });

        it('does not offer a new link to a visitor', async () => {
            server.use(capture('post', 'Security/verify-email', apiError(400, 'This link has expired.')).handler);
            renderLink('verify', '?token=old', null);

            await screen.findByText('This link has expired.');
            expect(screen.queryByRole('button', { name: 'Send a new verification email' })).not.toBeInTheDocument();
        });
    });

    describe('change', () => {
        it('confirms the new address', async () => {
            const confirm = capture('post', 'Security/confirm-email-change', ok({ message: 'Your email address was changed.' }));
            server.use(confirm.handler);
            renderLink('change', '?token=c1', authUser());

            expect(screen.getByRole('heading', { name: 'Confirm new email address' })).toBeInTheDocument();
            expect(await screen.findByRole('alert')).toHaveTextContent('Your email address was changed.');
            expect(confirm.last.body).toEqual({ token: 'c1' });
            expect(confirm.calls).toHaveLength(1);
        });

        it('tells a user whose session ended (another browser) to log in again', async () => {
            server.use(capture('post', 'Security/confirm-email-change', ok({ message: 'Your email address was changed.' })).handler);
            const { user } = renderLink('change', '?token=c1', null);

            const alert = await screen.findByRole('alert');
            expect(alert).toHaveTextContent('For your security you were logged out everywhere');
            await user.click(screen.getByRole('button', { name: 'Log in' }));
            expect(currentLocation()).toBe('/login');
        });

        it('explains how to get a new link after a failure', async () => {
            server.use(capture('post', 'Security/confirm-email-change', apiError(400, 'This link is no longer valid.')).handler);
            renderLink('change', '?token=bad', authUser());

            expect(await screen.findByText('This link is no longer valid.')).toBeInTheDocument();
            expect(screen.getByText(/changing your email again in your account settings/)).toBeInTheDocument();
            expect(screen.queryByText(/logged out everywhere/)).not.toBeInTheDocument();
        });
    });
});
