import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { AccountSettingsMenu } from '../../src/components/AccountSettingsMenu';
import { server } from '../support/server';
import { apiError, capture, ok } from '../support/api';
import { activeSubscription, authUser, securityStatus } from '../support/builders';
import { renderWithApp } from '../support/render';
import { stubLocation } from '../support/browser';

function renderMenu(onChangeUsername = vi.fn(async () => ({ success: true }))) {
    const result = renderWithApp(<AccountSettingsMenu username="alice" onChangeUsername={onChangeUsername} />, { user: authUser({ username: 'alice' }) });
    return { ...result, onChangeUsername };
}

async function open(user: ReturnType<typeof renderMenu>['user'], item: string) {
    await user.click(screen.getByRole('button', { name: 'Account settings' }));
    await user.click(screen.getByRole('menuitem', { name: item }));
}

describe('AccountSettingsMenu', () => {
    it('lists the account actions', async () => {
        const { user } = renderMenu();

        await user.click(screen.getByRole('button', { name: 'Account settings' }));

        expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual([
            'Change username', 'Change email', 'Change password', 'Security', 'Subscription', 'Log out of all devices',
        ]);
    });

    it('changes the username through the profile and closes the dialog', async () => {
        const { user, onChangeUsername } = renderMenu();
        await open(user, 'Change username');
        const dialog = await screen.findByRole('dialog', { name: 'Change Username' });
        const field = within(dialog).getByLabelText('New username');
        await new Promise(resolve => setTimeout(resolve, 50));

        await user.clear(field);
        await user.type(field, 'alicia{Enter}');

        expect(onChangeUsername).toHaveBeenCalledWith('alicia');
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('changes the email and says to check the new inbox', async () => {
        const change = capture('put', 'User/me/email', ok({ message: 'Check new@example.com to confirm the change.' }));
        server.use(change.handler);
        const { user } = renderMenu();
        await open(user, 'Change email');
        const dialog = await screen.findByRole('dialog', { name: 'Change Email' });

        await user.type(within(dialog).getByLabelText('New email'), 'new@example.com');
        await user.type(within(dialog).getByLabelText('Confirm new email'), 'new@example.com');
        await user.type(within(dialog).getByLabelText('Current password'), 'hunter22{Enter}');

        expect(await screen.findByText('Check new@example.com to confirm the change.')).toBeInTheDocument();
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(change.last.body).toEqual({ newEmail: 'new@example.com', currentPassword: 'hunter22' });
    });

    it('keeps the email dialog open with the API\'s error', async () => {
        server.use(capture('put', 'User/me/email', apiError(400, 'The password is wrong.')).handler);
        const { user } = renderMenu();
        await open(user, 'Change email');
        const dialog = await screen.findByRole('dialog', { name: 'Change Email' });

        await user.type(within(dialog).getByLabelText('New email'), 'new@example.com');
        await user.type(within(dialog).getByLabelText('Confirm new email'), 'new@example.com');
        await user.type(within(dialog).getByLabelText('Current password'), 'wrong{Enter}');

        expect(await within(dialog).findByText('The password is wrong.')).toBeInTheDocument();
    });

    it('changes the password', async () => {
        const change = capture('put', 'User/me/password', ok({ message: 'ok' }));
        server.use(change.handler);
        const { user } = renderMenu();
        await open(user, 'Change password');
        const dialog = await screen.findByRole('dialog', { name: 'Change Password' });

        await user.type(within(dialog).getByLabelText('Current password'), 'old-pass1');
        await user.type(within(dialog).getByLabelText('New password'), 'new-pass1');
        await user.type(within(dialog).getByLabelText('Confirm new password'), 'new-pass1{Enter}');

        expect(await screen.findByText('Password changed successfully.')).toBeInTheDocument();
        expect(change.last.body).toEqual({ currentPassword: 'old-pass1', newPassword: 'new-pass1' });
    });

    it('opens the security and subscription dialogs', async () => {
        server.use(
            capture('get', 'Security/status', ok(securityStatus({ email: 'alice@example.com' }))).handler,
            capture('get', 'Subscription/status', ok(activeSubscription())).handler,
        );
        const { user } = renderMenu();

        await open(user, 'Security');
        expect(await screen.findByText('alice@example.com')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

        await open(user, 'Subscription');
        expect(await screen.findByText('Renews automatically')).toBeInTheDocument();
    });

    it('logs out of every device and reloads the app', async () => {
        const location = stubLocation('https://localhost:5173/profile/alice/overview');
        const logoutAll = capture('post', 'Auth/logout-all', ok({}));
        server.use(logoutAll.handler);
        const { user } = renderMenu();
        await open(user, 'Log out of all devices');

        await user.click(await screen.findByRole('button', { name: 'Log out everywhere' }));

        await waitFor(() => expect(location.href).toBe('/'));
        expect(logoutAll.calls).toHaveLength(1);
    });

    it('stays when logging out everywhere fails', async () => {
        const location = stubLocation('https://localhost:5173/profile/alice/overview');
        server.use(capture('post', 'Auth/logout-all', apiError(500, 'Could not log out of all devices.')).handler);
        const { user } = renderMenu();
        await open(user, 'Log out of all devices');

        await user.click(await screen.findByRole('button', { name: 'Log out everywhere' }));

        expect(await screen.findByText('Could not log out of all devices.')).toBeInTheDocument();
        expect(location.href).toBe('https://localhost:5173/profile/alice/overview');
    });
});
