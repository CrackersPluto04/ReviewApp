import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { SecurityDialog } from '../../src/components/SecurityDialog';
import { server } from '../support/server';
import { apiError, capture, deferred, noContent, ok } from '../support/api';
import { authUser, securityStatus } from '../support/builders';
import { renderWithApp } from '../support/render';
import { knownIssue } from '../support/known-issue';

function serveStatus(...statuses: ReturnType<typeof securityStatus>[]) {
    let call = 0;
    const endpoint = capture('get', 'Security/status', () => HttpResponse.json(statuses[Math.min(call++, statuses.length - 1)]));
    server.use(endpoint.handler);
    return endpoint;
}

// The 2FA fields mount later and get the dialog title's id from Preact's useId (W19), so their <label for> points
// at the title: they are found through the label element instead
function field(label: string) {
    const element = screen.getAllByText(label, { selector: 'label' })[0];
    return element.parentElement!.querySelector('input') as HTMLInputElement;
}

async function findField(label: string) {
    await screen.findAllByText(label, { selector: 'label' });
    return field(label);
}

function renderDialog() {
    return renderWithApp(<SecurityDialog open onClose={vi.fn()} />, { user: authUser() });
}

describe('SecurityDialog', () => {
    it('loads the status when opened', async () => {
        const response = deferred(() => ok(securityStatus({ email: 'alice@example.com' })));
        const status = capture('get', 'Security/status', () => response.promise);
        server.use(status.handler);
        renderDialog();

        expect(await screen.findByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('alice@example.com')).toBeInTheDocument();
        expect(screen.getByText('Verified')).toBeInTheDocument();
        expect(screen.getByText('Off')).toBeInTheDocument();
        expect(status.calls).toHaveLength(1);
    });

    it('does not load while closed', async () => {
        const status = serveStatus(securityStatus());
        renderWithApp(<SecurityDialog open={false} onClose={vi.fn()} />);

        await new Promise(resolve => setTimeout(resolve, 50));
        expect(status.calls).toHaveLength(0);
    });

    it('shows a status that could not be loaded', async () => {
        server.use(capture('get', 'Security/status', apiError(500, 'Could not load your security settings.')).handler);
        renderDialog();

        expect(await screen.findByText('Could not load your security settings.')).toBeInTheDocument();
    });

    describe('email', () => {
        it('lets an unverified user resend the verification and blocks 2FA', async () => {
            serveStatus(securityStatus({ isEmailConfirmed: false }));
            server.use(capture('post', 'Security/resend-verification', ok({ message: 'Verification email sent.' })).handler);
            const { user } = renderDialog();

            expect(await screen.findByText('Not verified')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Enable two-factor authentication' })).toBeDisabled();
            expect(screen.getByText('Verify your email address first.')).toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: 'Send verification email' }));
            expect(await screen.findByText('Verification email sent.')).toBeInTheDocument();
        });

        it('shows a pending email change and cancels it', async () => {
            serveStatus(securityStatus({ pendingEmail: 'new@example.com' }), securityStatus({ pendingEmail: null }));
            const cancel = capture('delete', 'Security/pending-email', noContent());
            server.use(cancel.handler);
            const { user } = renderDialog();

            expect(await screen.findByText('new@example.com')).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: 'Cancel change' }));

            expect(await screen.findByText('The pending email change was cancelled.')).toBeInTheDocument();
            await waitFor(() => expect(screen.queryByText('new@example.com')).not.toBeInTheDocument());
            expect(cancel.calls).toHaveLength(1);
        });

        it('shows why cancelling the email change failed', async () => {
            serveStatus(securityStatus({ pendingEmail: 'new@example.com' }));
            server.use(capture('delete', 'Security/pending-email', apiError(400, 'There is no pending email change.')).handler);
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Cancel change' }));

            expect(await screen.findByText('There is no pending email change.')).toBeInTheDocument();
            expect(screen.getByText('new@example.com')).toBeInTheDocument();
        });
    });

    describe('two-factor authentication', () => {
        it('enables it: password, emailed code, then refreshed status and user', async () => {
            serveStatus(securityStatus({ isTwoFactorEnabled: false }), securityStatus({ isTwoFactorEnabled: true }));
            const request = capture('post', 'Security/2fa/enable/request', ok({ message: 'We sent a code to your email.' }));
            const confirm = capture('post', 'Security/2fa/enable/confirm', ok({ message: 'Two-factor authentication is on.' }));
            let checks = 0;
            server.use(request.handler, confirm.handler);
            const { user } = renderDialog();
            server.use(capture('get', 'Auth/check-auth', () => { checks++; return ok(authUser()); }).handler);

            await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
            expect(screen.getByText(/Recovery codes aren't available yet/)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Send code' })).toBeDisabled();
            await user.type(field('Current password'), 'hunter22{Enter}');

            expect(await screen.findByText('We sent a code to your email.')).toBeInTheDocument();
            expect(request.last.body).toEqual({ currentPassword: 'hunter22' });
            await user.type(field('Code'), '12x3456789');
            expect(field('Code')).toHaveValue('123456');
            await user.click(screen.getByRole('button', { name: 'Enable' }));

            expect(await screen.findByText('Two-factor authentication is on.')).toBeInTheDocument();
            expect(confirm.last.body).toEqual({ code: '123456' });
            expect(await screen.findByText('On')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Disable two-factor authentication' })).toBeInTheDocument();
            await waitFor(() => expect(checks).toBeGreaterThan(0));
        });

        it('disables it the same way', async () => {
            serveStatus(securityStatus({ isTwoFactorEnabled: true }), securityStatus({ isTwoFactorEnabled: false }));
            const request = capture('post', 'Security/2fa/disable/request', ok({ message: 'Code sent.' }));
            const confirm = capture('post', 'Security/2fa/disable/confirm', ok({ message: 'Two-factor authentication is off.' }));
            server.use(request.handler, confirm.handler);
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Disable two-factor authentication' }));
            expect(screen.queryByText(/Recovery codes/)).not.toBeInTheDocument();
            await user.type(field('Current password'), 'hunter22');
            await user.click(screen.getByRole('button', { name: 'Send code' }));
            await user.type(await findField('Code'), '654321{Enter}');

            expect(await screen.findByText('Two-factor authentication is off.')).toBeInTheDocument();
            expect(confirm.calls).toHaveLength(1);
        });

        it('stays on the password step when it is wrong', async () => {
            serveStatus(securityStatus());
            server.use(capture('post', 'Security/2fa/enable/request', apiError(400, 'The password is wrong.')).handler);
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
            await user.type(field('Current password'), 'wrong{Enter}');

            expect(await screen.findByText('The password is wrong.')).toBeInTheDocument();
            expect(field('Current password')).toBeInTheDocument();
        });

        it('stays on the code step when the code is wrong, and can resend it', async () => {
            serveStatus(securityStatus());
            const request = capture('post', 'Security/2fa/enable/request', ok({ message: 'Code sent.' }));
            server.use(request.handler, capture('post', 'Security/2fa/enable/confirm', apiError(400, 'The code is wrong.')).handler);
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
            await user.type(field('Current password'), 'hunter22{Enter}');
            await user.type(await findField('Code'), '000000');
            await user.click(screen.getByRole('button', { name: 'Enable' }));

            expect(await screen.findByText('The code is wrong.')).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: 'Resend code' }));
            await waitFor(() => expect(request.calls).toHaveLength(2));
            // The kept password is sent again
            expect(request.last.body).toEqual({ currentPassword: 'hunter22' });
            expect(field('Code')).toHaveValue('');
        });

        it('cancels a step and forgets the password', async () => {
            serveStatus(securityStatus());
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
            await user.type(field('Current password'), 'hunter22');
            await user.click(screen.getByRole('button', { name: 'Cancel' }));

            expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: 'Enable two-factor authentication' }));
            expect(field('Current password')).toHaveValue('');
        });

        it('needs a full code before confirming', async () => {
            serveStatus(securityStatus());
            server.use(capture('post', 'Security/2fa/enable/request', ok({ message: 'Code sent.' })).handler);
            const { user } = renderDialog();

            await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
            await user.type(field('Current password'), 'hunter22{Enter}');
            await user.type(await findField('Code'), '123');

            expect(screen.getByRole('button', { name: 'Enable' })).toBeDisabled();
        });
    });

    it('closes the feedback and the dialog', async () => {
        serveStatus(securityStatus({ isEmailConfirmed: false }));
        server.use(capture('post', 'Security/resend-verification', ok({ message: 'Sent.' })).handler);
        const onClose = vi.fn();
        const { user } = renderWithApp(<SecurityDialog open onClose={onClose} />, { user: authUser() });

        await user.click(await screen.findByRole('button', { name: 'Send verification email' }));
        const alert = (await screen.findByText('Sent.')).closest('.MuiAlert-root') as HTMLElement;
        await user.click(within(alert).getByRole('button', { name: 'Close' }));
        expect(screen.queryByText('Sent.')).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Close' }));
        expect(onClose).toHaveBeenCalled();
    });

    knownIssue('W19', 'the two-factor code field is labelled by its own label', async () => {
        serveStatus(securityStatus());
        server.use(capture('post', 'Security/2fa/enable/request', ok({ message: 'Code sent.' })).handler);
        const { user } = renderDialog();

        await user.click(await screen.findByRole('button', { name: 'Enable two-factor authentication' }));
        await user.type(field('Current password'), 'hunter22{Enter}');
        await screen.findByText('Code sent.');

        // Its id is the dialog title's, so <label for> (and getByLabelText) resolves to the <h2>
        expect(screen.getByLabelText('Code')).toBe(field('Code'));
    });
});
