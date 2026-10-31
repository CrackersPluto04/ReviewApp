import { describe, expect, it, vi } from 'vitest';
import { act, screen } from '@testing-library/preact';
import { UserSearchBar } from '../../src/components/UserSearchBar';
import { server } from '../support/server';
import { apiError, capture, ok } from '../support/api';
import { compactUser } from '../support/builders';
import { currentLocation, flush, paint, renderWithApp, useFakeTimers } from '../support/render';

function serveSearch(users = [compactUser({ username: 'bob' }), compactUser({ username: 'bobby' })]) {
    const search = capture('get', 'User/search', ok(users));
    server.use(search.handler);
    return search;
}

describe('UserSearchBar', () => {
    it('searches once the user stops typing for 300 ms', async () => {
        const search = serveSearch();
        const { user } = renderWithApp(<UserSearchBar />);
        useFakeTimers(undefined, { frozen: true });

        await user.type(screen.getByPlaceholderText('Find users...'), 'bo');
        // The 300 ms start when the effect runs, somewhere within paint()'s 50 ms
        await paint();
        await act(async () => { await vi.advanceTimersByTimeAsync(250); });
        await flush();
        expect(search.calls).toHaveLength(0);

        await act(async () => { await vi.advanceTimersByTimeAsync(50); });
        await flush();
        expect(search.calls).toHaveLength(1);
        expect(search.last.url.searchParams.get('q')).toBe('bo');
        expect(screen.getByText('bobby')).toBeInTheDocument();
    });

    it('sends one request for fast typing', async () => {
        const search = serveSearch();
        const { user } = renderWithApp(<UserSearchBar />);
        useFakeTimers(undefined, { frozen: true });

        await user.type(screen.getByPlaceholderText('Find users...'), 'bobby');
        await paint();
        await act(async () => { await vi.advanceTimersByTimeAsync(300); });
        await flush();

        expect(search.calls).toHaveLength(1);
        expect(search.last.url.searchParams.get('q')).toBe('bobby');
    });

    it('does not search for blank input', async () => {
        const search = serveSearch();
        const { user } = renderWithApp(<UserSearchBar />);
        useFakeTimers(undefined, { frozen: true });

        await user.type(screen.getByPlaceholderText('Find users...'), '   ');
        await paint();
        await act(async () => { await vi.advanceTimersByTimeAsync(1_000); });
        await flush();

        expect(search.calls).toHaveLength(0);
    });

    it('opens the chosen user\'s profile and clears the input', async () => {
        serveSearch([compactUser({ username: 'b b' })]);
        const { user } = renderWithApp(<UserSearchBar />);

        await user.type(screen.getByPlaceholderText('Find users...'), 'b');
        // The option's name starts with the avatar's initial
        await user.click(await screen.findByRole('option', { name: /b b$/ }));

        expect(currentLocation()).toBe('/profile/b%20b/overview');
        expect(screen.getByPlaceholderText('Find users...')).toHaveValue('');
    });

    it('shows no options when the search fails', async () => {
        server.use(capture('get', 'User/search', apiError(500, 'Down')).handler);
        const { user } = renderWithApp(<UserSearchBar />);

        await user.type(screen.getByPlaceholderText('Find users...'), 'bo');

        expect(await screen.findByText('No options')).toBeInTheDocument();
    });
});
