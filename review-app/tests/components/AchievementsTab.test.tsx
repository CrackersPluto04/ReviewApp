import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { AchievementsTab } from '../../src/components/AchievementsTab';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { achievement } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';

const PATH = '/profile/:username/achievements';

const list = [
    achievement({ groupCode: 'A', title: 'Zebra', categories: ['Movie'], currentProgress: 0 }),
    achievement({ groupCode: 'B', title: 'Apple', categories: ['Review'], currentProgress: 4 }),
    achievement({ groupCode: 'C', title: 'Mango', categories: ['Music', 'Review'], currentProgress: 60 }),
];

function serveAchievements(response: Response | (() => Promise<Response>) = ok(list)) {
    const endpoint = capture('get', 'Achievement/:username', response as any);
    server.use(endpoint.handler);
    return endpoint;
}

const titles = () => screen.getAllByRole('button', { name: /tiers unlocked/ }).map(b => b.getAttribute('aria-label')!.split(',')[0]);
const chip = (name: string) => screen.getByRole('button', { name });

function renderTab(search = '') {
    return renderWithApp(<AchievementsTab />, { path: PATH, route: `/profile/bob/achievements${search}` });
}

describe('AchievementsTab', () => {
    it('loads the user\'s achievements, unlocked ones first, with the tier total', async () => {
        const endpoint = serveAchievements();
        renderTab();

        expect(await screen.findByText('4 of 9 tiers earned')).toBeInTheDocument();
        expect(titles()).toEqual(['Apple', 'Mango', 'Zebra']);
        expect(endpoint.last.url.pathname).toBe('/api/Achievement/bob');
    });

    it('shows a spinner while loading', async () => {
        const response = deferred(() => ok(list));
        serveAchievements(() => response.promise);
        renderTab();

        expect(screen.getByRole('progressbar')).toBeInTheDocument();
        response.release();
        await screen.findByText('4 of 9 tiers earned');
    });

    it('says when there are none and when loading failed', async () => {
        serveAchievements(ok([]));
        const empty = renderTab();
        expect(await screen.findByText('No achievements yet.')).toBeInTheDocument();
        expect(screen.queryByText(/tiers earned/)).not.toBeInTheDocument();
        empty.unmount();

        server.use(capture('get', 'Achievement/:username', apiError(404, 'User not found.')).handler);
        renderTab();
        expect(await screen.findByText('User not found.')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Reset filters' })).not.toBeInTheDocument();
    });

    it('reads the filters from the URL, ignoring unknown values', async () => {
        serveAchievements();
        renderTab('?status=unlocked&status=bogus&category=Review&category=Nope&sort=title');

        await screen.findByText('4 of 9 tiers earned');
        expect(titles()).toEqual(['Apple', 'Mango']);
        expect(chip('Unlocked')).toHaveAttribute('aria-pressed', 'true');
        expect(chip('Review')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByRole('combobox')).toHaveTextContent('Title (A-Z)');
    });

    it('treats an unknown sort as the default', async () => {
        serveAchievements();
        renderTab('?sort=random');

        await screen.findByText('4 of 9 tiers earned');
        expect(titles()).toEqual(['Apple', 'Mango', 'Zebra']);
    });

    it('filters as chips are toggled, keeping them in the URL', async () => {
        serveAchievements();
        const { user } = renderTab();
        await screen.findByText('4 of 9 tiers earned');

        await user.click(chip('Locked'));
        expect(currentLocation()).toBe('/profile/bob/achievements?status=locked');
        expect(titles()).toEqual(['Zebra']);

        await user.click(chip('In progress'));
        expect(titles()).toEqual(['Apple', 'Zebra']);

        await user.click(chip('Locked'));
        expect(currentLocation()).toBe('/profile/bob/achievements?status=in-progress');
        expect(titles()).toEqual(['Apple']);
    });

    it('sorts with the picker', async () => {
        serveAchievements();
        const { user } = renderTab();
        await screen.findByText('4 of 9 tiers earned');

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Most progress' }));

        expect(currentLocation()).toBe('/profile/bob/achievements?sort=progress');
        expect(titles()).toEqual(['Mango', 'Apple', 'Zebra']);

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Unlocked first' }));
        expect(currentLocation()).toBe('/profile/bob/achievements');
    });

    it('offers a reset when nothing matches', async () => {
        serveAchievements();
        const { user } = renderTab('?status=locked&category=Music');

        expect(await screen.findByText('No achievements match these filters.')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Reset filters' }));

        expect(currentLocation()).toBe('/profile/bob/achievements');
        await waitFor(() => expect(titles()).toHaveLength(3));
    });

    it('turns every card back to its front when the filters change', async () => {
        serveAchievements();
        const { user } = renderTab();
        await screen.findByText('4 of 9 tiers earned');
        await user.click(screen.getByRole('button', { name: /^Apple/ }));
        expect(screen.getByRole('button', { name: /^Apple/ })).toHaveAttribute('aria-pressed', 'true');

        await user.click(chip('Review'));

        expect(screen.getByRole('button', { name: /^Apple/ })).toHaveAttribute('aria-pressed', 'false');
    });
});
