import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { NotificationBell } from '../../src/components/NotificationBell';
import { server } from '../support/server';
import { capture, ok } from '../support/api';
import { authUser, notification, notificationPage } from '../support/builders';
import { renderWithApp } from '../support/render';

function serveCount(count: number) {
    server.use(capture('get', 'Notification/unseen-count', ok({ count })).handler);
}

function serveList(items: ReturnType<typeof notification>[], overrides: Record<string, any> = {}) {
    server.use(capture('get', 'Notification', HttpResponse.json(notificationPage(items, overrides))).handler);
}

describe('NotificationBell', () => {
    it('shows the unseen count and opens the dialog', async () => {
        serveCount(3);
        serveList([notification({ title: 'Hi' })], { unseenCount: 3 });
        const { user } = renderWithApp(<NotificationBell />, { user: authUser() });

        const bell = await screen.findByRole('button', { name: 'Notifications, 3 unseen' });
        expect(bell).toHaveAttribute('aria-haspopup', 'dialog');
        await user.click(bell);

        expect(await screen.findByText('Hi')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Close notifications' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('caps the badge at 99+', async () => {
        serveCount(150);
        renderWithApp(<NotificationBell />, { user: authUser() });

        await screen.findByRole('button', { name: 'Notifications, 150 unseen' });
        expect(screen.getByText('99+')).toBeInTheDocument();
    });

    it('has a plain label without unseen notifications', async () => {
        serveCount(0);
        renderWithApp(<NotificationBell />, { user: authUser() });

        expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
        expect(screen.getByTestId('NotificationsNoneIcon')).toBeInTheDocument();
    });
});
