import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { Button, Dialog, DialogTitle, Snackbar } from '@mui/material';
import { useState } from 'preact/hooks';
import { http, HttpResponse } from 'msw';
import { server, takeUnhandledRequests } from './server';
import { apiUrl, capture } from './api';
import { setupUser, useFakeTimers } from './render';
import { TEST_LOCALE } from './locale';
import { apiFetch } from '../../src/services/apiClient';

/**
 * Guards for the test environment itself: if one of these fails, the other failures are probably not real.
 */
describe('test environment', () => {
    describe('Preact as React', () => {
        function DialogDemo() {
            const [open, setOpen] = useState(false);
            return <>
                <Button onClick={() => setOpen(true)}>Open</Button>
                <Dialog open={open} onClose={() => setOpen(false)}>
                    <DialogTitle>Hello</DialogTitle>
                    <Button onClick={() => setOpen(false)}>Close</Button>
                </Dialog>
            </>;
        }

        it('renders and drives MUI components (transitions, portals, focus trap) through preact/compat', async () => {
            const user = setupUser();
            render(<DialogDemo />);

            await user.click(screen.getByRole('button', { name: 'Open' }));
            expect(await screen.findByRole('dialog')).toHaveTextContent('Hello');

            await user.click(screen.getByRole('button', { name: 'Close' }));
            await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        });

        it('auto-hides a Snackbar with fake timers', async () => {
            useFakeTimers();
            const onClose = vi.fn();
            render(<Snackbar open autoHideDuration={5000} onClose={onClose} message="Saved" />);

            expect(screen.getByText('Saved')).toBeInTheDocument();
            await vi.advanceTimersByTimeAsync(5000);

            expect(onClose).toHaveBeenCalledWith(null, 'timeout');
        });
    });

    describe('locale and time zone', () => {
        it('formats with the pinned en-US locale when no locale is given', () => {
            expect(Intl.DateTimeFormat().resolvedOptions().locale).toBe(TEST_LOCALE);
            expect(new Date('2026-03-10T12:00:00Z').toLocaleDateString()).toBe('3/10/2026');
            expect(new Intl.NumberFormat(undefined, { style: 'currency', currency: 'EUR' }).format(4.99)).toBe('€4.99');
            expect((1234.5).toLocaleString()).toBe('1,234.5');
            expect(['b', 'a', 'B'].sort((x, y) => x.localeCompare(y))).toEqual(['a', 'b', 'B']);
        });

        it('keeps an explicit locale', () => {
            expect(new Date('2026-03-10T12:00:00Z').toLocaleDateString('de-DE')).toBe('10.3.2026');
        });

        it('runs in UTC', () => {
            expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('UTC');
            expect(new Date('2026-01-01T00:30:00Z').getHours()).toBe(0);
        });
    });

    describe('fake network', () => {
        it('answers the API base URL from the handlers', async () => {
            server.use(http.get(apiUrl('Probe'), () => HttpResponse.json({ ok: true })));

            const response = await apiFetch(apiUrl('Probe'));

            expect(await response.json()).toEqual({ ok: true });
        });

        it('records what the app sent, including credentials', async () => {
            const probe = capture('post', 'Probe');
            server.use(probe.handler);

            await apiFetch(apiUrl('Probe?x=1'), {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ a: 1 }),
            });

            expect(probe.calls).toHaveLength(1);
            expect(probe.last.url.searchParams.get('x')).toBe('1');
            expect(probe.last.body).toEqual({ a: 1 });
            expect(probe.last.credentials).toBe('include');
            expect(probe.last.headers.get('Content-Type')).toBe('application/json');
        });

        it('fails a request without a handler and reports it (the setup file then fails the test)', async () => {
            await expect(fetch(apiUrl('NoSuchHandler'))).rejects.toThrow();

            // Taken here, so this test itself stays green
            expect(takeUnhandledRequests()).toEqual([`GET ${apiUrl('NoSuchHandler')}`]);
        });
    });
});
