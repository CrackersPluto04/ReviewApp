import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { App } from '../src/App';
import { server } from './support/server';
import { capture, ok } from './support/api';
import { activeSubscription, authUser, movie, paged, plan, profileReview, unlocked, userProfile } from './support/builders';
import { currentLocation, renderApp } from './support/render';
import { serveMediaReviews, serveStats } from './support/handlers';

/**
 * The route table of src/App.tsx: every URL renders its page inside the shared chrome.
 * Page behaviour is tested in tests/pages; this only proves the wiring.
 */
describe('App routes', () => {
    it.each([
        ['/login', () => screen.findByRole('heading', { name: 'Login to your account' })],
        ['/home', () => screen.findByRole('heading', { name: /Welcome to THE Review App!/ })],
        ['/search', () => screen.findByText('Type something above to start searching!')],
        ['/premium', () => screen.findByRole('heading', { name: 'THE Review App Premium' })],
        ['/subscription/return?status=cancelled', () => screen.findByRole('heading', { name: 'Checkout cancelled' })],
        ['/verify-email', () => screen.findByRole('heading', { name: 'Email verification' })],
        ['/confirm-email-change', () => screen.findByRole('heading', { name: 'Confirm new email address' })],
    ])('%s', async (route, findPage) => {
        server.use(capture('get', 'Subscription/plan', ok(plan())).handler);
        renderApp(route);

        expect(await findPage()).toBeInTheDocument();
        // Every page has the header and the footer
        expect(screen.getByRole('banner')).toHaveTextContent('THE Review App');
        expect(screen.getByText(/All rights reserved/)).toBeInTheDocument();
    });

    it('/discover and /discover/music', async () => {
        serveStats();
        server.use(
            capture('get', 'Media/discover/series', ok(paged([]))).handler,
            capture('get', 'Media/discover/music', ok(paged([]))).handler,
        );
        const first = renderApp('/discover?type=series');
        expect(await screen.findByRole('heading', { name: 'Discover Series' })).toBeInTheDocument();
        first.unmount();

        renderApp('/discover/music');
        expect(await screen.findByRole('heading', { name: 'Discover Music' })).toBeInTheDocument();
    });

    it('/media/:mediaType/:externalApiId', async () => {
        server.use(capture('get', 'Media/media/movie/tmdb-1', ok(movie({ title: 'Arrival' }))).handler);
        serveMediaReviews([]);
        renderApp('/media/movie/tmdb-1');

        expect(await screen.findByRole('heading', { name: 'Arrival' })).toBeInTheDocument();
    });

    it('/review/:reviewId', async () => {
        server.use(
            capture('get', 'Review/12', ok(profileReview({ id: 12, username: 'bob' }))).handler,
            capture('get', 'ReviewReply/1', ok({ reviewID: 12, reply: { id: 1 }, parentReply: null })).handler,
            capture('get', 'ReviewReply', ok({ items: [], hasMore: false })).handler,
        );
        renderApp('/review/12?reply=1');

        expect(await screen.findByRole('heading', { name: "bob's review" })).toBeInTheDocument();
    });

    it.each([
        ['reviews', () => screen.findByRole('heading', { name: 'Reviews (0)' })],
        ['collections', () => screen.findByText('No collections yet.')],
        ['achievements', () => screen.findByText('No achievements yet.')],
    ])('/profile/:username/%s', async (tab, findTab) => {
        server.use(
            capture('get', 'User/bob', ok(userProfile({ username: 'bob' }))).handler,
            capture('get', 'User/bob/reviews', ok(paged([]))).handler,
            capture('get', 'User/bob/collections', ok([])).handler,
            capture('get', 'Achievement/bob', ok([])).handler,
        );
        renderApp(`/profile/bob/${tab}`);

        expect(await findTab()).toBeInTheDocument();
        expect(screen.getByRole('tab', { selected: true })).toHaveTextContent(new RegExp(tab, 'i'));
    });

    it.each(['/', '/nope', '/profile'])('sends %s to /home', async route => {
        renderApp(route);

        expect(await screen.findByRole('heading', { name: /Welcome to THE Review App!/ })).toBeInTheDocument();
        expect(currentLocation()).toBe('/home');
    });
});

describe('App', () => {
    it('starts in light mode and remembers the toggled theme', async () => {
        const { user, unmount } = renderApp('/home');
        await screen.findByRole('heading', { name: /Welcome/ });
        expect(screen.getByTestId('Brightness4Icon')).toBeInTheDocument();

        await user.click(screen.getByTestId('Brightness4Icon').closest('button')!);

        expect(screen.getByTestId('Brightness7Icon')).toBeInTheDocument();
        expect(localStorage.getItem('theme')).toBe('dark');
        unmount();

        renderApp('/home');
        expect(await screen.findByTestId('Brightness7Icon')).toBeInTheDocument();
    });

    it('renders with the browser router at the current URL', async () => {
        window.history.replaceState(null, '', '/premium');
        server.use(capture('get', 'Subscription/plan', ok(plan())).handler);
        render(<App />);

        expect(await screen.findByRole('heading', { name: 'THE Review App Premium' })).toBeInTheDocument();
        window.history.replaceState(null, '', '/');
    });

    it('shows an achievement unlocked by any request as a toast', async () => {
        serveStats();
        server.use(
            capture('get', 'Media/discover/movie', ok(paged([movie({ title: 'Arrival', externalApiID: 'tmdb-1' })]))).handler,
            capture('get', 'User/:username/collections', ok([{ id: 7, name: 'Favourites', isDefault: true, isOwner: true, visibilityLevel: 0, createdAt: '', mediaCount: 0 }])).handler,
            capture('post', 'Collection/7/media', () => HttpResponse.json({ message: 'Added' }, {
                headers: { 'X-Unlocked-Achievements': JSON.stringify([unlocked({ title: 'Collector', tier: 'Bronze' })]) },
            })).handler,
        );
        const { user } = renderApp('/discover', { user: authUser() });
        // MediaCard logs the favourite it adds (W9)
        vi.spyOn(console, 'log').mockImplementation(() => {});

        await user.click(await screen.findByRole('button', { name: 'Add to Favorites' }));

        expect(await screen.findByText('Collector (Bronze)')).toBeInTheDocument();
        expect(screen.getByText('Added to Favourites!')).toBeInTheDocument();
    });

    it('drops the session everywhere on a 401', async () => {
        server.use(
            capture('get', 'Subscription/plan', ok(plan())).handler,
            capture('get', 'Subscription/status', ok(activeSubscription())).handler,
            capture('get', 'Subscription/entitlement-preview', HttpResponse.json({ error: 'Unauthorized' }, { status: 401 })).handler,
        );
        const { user } = renderApp('/premium', { user: authUser() });

        await user.click(await screen.findByRole('button', { name: 'Open the early access preview' }));

        await waitFor(() => expect(screen.getByRole('button', { name: 'Login' })).toBeInTheDocument());
        expect(screen.queryByRole('button', { name: 'Open the early access preview' })).not.toBeInTheDocument();
    });
});
