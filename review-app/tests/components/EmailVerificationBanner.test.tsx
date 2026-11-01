import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { EmailVerificationBanner } from '../../src/components/EmailVerificationBanner';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { authUser } from '../support/builders';
import { renderWithApp } from '../support/render';
import { HttpResponse } from 'msw';
import { knownIssue } from '../support/known-issue';

const unverified = () => authUser({ isEmailConfirmed: false });
const banner = () => screen.queryByText(/Please verify your email address/);

describe('EmailVerificationBanner', () => {
    it('reminds an unverified user', async () => {
        renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        expect(await screen.findByText(/Please verify your email address/)).toBeInTheDocument();
    });

    it.each([
        ['a visitor', null],
        ['a verified user', authUser({ isEmailConfirmed: true })],
    ])('is hidden for %s', async (_, user) => {
        renderWithApp(<EmailVerificationBanner />, { user });
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(banner()).not.toBeInTheDocument();
    });

    it('is hidden on the verification page itself', async () => {
        renderWithApp(<EmailVerificationBanner />, { user: unverified(), route: '/verify-email?token=x' });
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(banner()).not.toBeInTheDocument();
    });

    knownIssue('W5', 'the reminder can be dismissed', async () => {
        const { user } = renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        await screen.findByText(/Please verify/);

        // MUI's Alert renders no close button when it has an action, so onClose is unreachable
        await user.click(screen.getByRole('button', { name: 'Close' }));

        expect(banner()).not.toBeInTheDocument();
    });

    it('stays dismissed for the session (closed after a resend)', async () => {
        server.use(capture('post', 'Security/resend-verification', ok({ message: 'Sent.' })).handler);
        const first = renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        await screen.findByText(/Please verify/);
        await first.user.click(screen.getByRole('button', { name: 'Resend email' }));

        await first.user.click(await screen.findByRole('button', { name: 'Close' }));
        expect(screen.queryByText(/Sent\./)).not.toBeInTheDocument();
        expect(sessionStorage.getItem('emailVerificationBannerDismissed')).toBe('true');

        first.unmount();
        renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(banner()).not.toBeInTheDocument();
    });

    it('stays hidden when it was dismissed earlier in the session', async () => {
        sessionStorage.setItem('emailVerificationBannerDismissed', 'true');
        renderWithApp(<EmailVerificationBanner />, { user: unverified() });

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(banner()).not.toBeInTheDocument();
    });

    it('resends the email and confirms it', async () => {
        const response = deferred(() => ok({ message: 'Verification email sent.' }));
        const resend = capture('post', 'Security/resend-verification', () => response.promise);
        server.use(resend.handler);
        const { user } = renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        await screen.findByText(/Please verify/);

        await user.click(screen.getByRole('button', { name: 'Resend email' }));
        expect(screen.getByRole('button', { name: 'Resend email' })).toBeDisabled();
        response.release();

        const alert = await screen.findByText(/Verification email sent\./);
        expect(alert).toHaveTextContent("Don't forget to check your spam folder.");
        expect(resend.calls).toHaveLength(1);
    });

    it('shows why the resend failed', async () => {
        server.use(capture('post', 'Security/resend-verification', apiError(429, 'Please wait before asking again.')).handler);
        const { user } = renderWithApp(<EmailVerificationBanner />, { user: unverified() });
        await screen.findByText(/Please verify/);

        await user.click(screen.getByRole('button', { name: 'Resend email' }));

        const alert = await screen.findByText('Please wait before asking again.');
        expect(alert).not.toHaveTextContent('spam');
        await user.click(screen.getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(screen.queryByText('Please wait before asking again.')).not.toBeInTheDocument());
    });

    it('works when session storage is unavailable', async () => {
        const getItem = Storage.prototype.getItem;
        const setItem = Storage.prototype.setItem;
        // Only session storage fails (the test harness keeps the theme in local storage)
        Storage.prototype.getItem = function (this: Storage, key: string) { if (this === sessionStorage) throw new Error('denied'); return getItem.call(this, key); };
        Storage.prototype.setItem = function (this: Storage, key: string, value: string) { if (this === sessionStorage) throw new Error('denied'); setItem.call(this, key, value); };
        try {
            server.use(capture('post', 'Security/resend-verification', HttpResponse.json({ message: 'Sent.' })).handler);
            const { user } = renderWithApp(<EmailVerificationBanner />, { user: unverified() });
            await screen.findByText(/Please verify/);
            await user.click(screen.getByRole('button', { name: 'Resend email' }));

            await user.click(await screen.findByRole('button', { name: 'Close' }));
            expect(screen.queryByText(/Sent\./)).not.toBeInTheDocument();
        } finally {
            Storage.prototype.getItem = getItem;
            Storage.prototype.setItem = setItem;
        }
    });
});
