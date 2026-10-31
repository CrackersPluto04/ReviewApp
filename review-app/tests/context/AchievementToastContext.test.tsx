import { describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { AchievementToastProvider } from '../../src/context/AchievementToastContext';
import { apiFetch } from '../../src/services/apiClient';
import { server } from '../support/server';
import { apiUrl, capture } from '../support/api';
import { unlocked } from '../support/builders';
import { setupUser, useFakeTimers } from '../support/render';

function serveUnlocks(...tiers: ReturnType<typeof unlocked>[]) {
    server.use(capture('post', 'Review', () =>
        HttpResponse.json({}, { headers: { 'X-Unlocked-Achievements': JSON.stringify(tiers) } })).handler);
}

const toast = () => screen.queryByRole('alert');

describe('AchievementToastContext', () => {
    it('shows a toast for an unlock reported by any API response', async () => {
        render(<AchievementToastProvider><p>page</p></AchievementToastProvider>);
        serveUnlocks(unlocked({ title: 'Critic', tier: 'Silver' }));

        await act(() => apiFetch(apiUrl('Review'), { method: 'POST' }));

        const alert = await screen.findByRole('alert');
        expect(alert).toHaveTextContent('Achievement unlocked!');
        expect(alert).toHaveTextContent('Critic (Silver)');
        expect(screen.getByText('page')).toBeInTheDocument();
    });

    it('shows several unlocks one after the other', async () => {
        useFakeTimers();
        render(<AchievementToastProvider><p>page</p></AchievementToastProvider>);
        serveUnlocks(unlocked({ title: 'Critic', tier: 'Bronze' }), unlocked({ title: 'Critic', tier: 'Silver' }));

        await act(() => apiFetch(apiUrl('Review'), { method: 'POST' }));
        expect(await screen.findByRole('alert')).toHaveTextContent('Critic (Bronze)');

        // 5 s on screen, then the exit transition
        await act(() => vi.advanceTimersByTimeAsync(5_000));
        await act(() => vi.advanceTimersByTimeAsync(1_000));

        await waitFor(() => expect(toast()).toHaveTextContent('Critic (Silver)'));

        await act(() => vi.advanceTimersByTimeAsync(6_000));
        await waitFor(() => expect(toast()).not.toBeInTheDocument());
    });

    it('closes with the close button', async () => {
        render(<AchievementToastProvider><p>page</p></AchievementToastProvider>);
        serveUnlocks(unlocked());
        await act(() => apiFetch(apiUrl('Review'), { method: 'POST' }));
        await screen.findByRole('alert');

        await setupUser().click(screen.getByRole('button', { name: /close/i }));

        await waitFor(() => expect(toast()).not.toBeInTheDocument());
    });

    it('stays open on a click elsewhere', async () => {
        render(<AchievementToastProvider><p>page</p></AchievementToastProvider>);
        serveUnlocks(unlocked());
        await act(() => apiFetch(apiUrl('Review'), { method: 'POST' }));
        await screen.findByRole('alert');

        await setupUser().click(screen.getByText('page'));

        expect(toast()).toBeInTheDocument();
    });

    it('stops listening after unmounting', async () => {
        const { unmount } = render(<AchievementToastProvider><p>page</p></AchievementToastProvider>);
        unmount();
        serveUnlocks(unlocked());

        await expect(apiFetch(apiUrl('Review'), { method: 'POST' })).resolves.toBeTruthy();
        expect(toast()).not.toBeInTheDocument();
    });
});
