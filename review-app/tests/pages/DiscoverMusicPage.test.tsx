import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { DiscoverMusicPage } from '../../src/pages/DiscoverMusicPage';
import { server } from '../support/server';
import { apiError, capture } from '../support/api';
import { album, movie, paged, series } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';

function serve(what: 'movie' | 'series' | 'music', respond: (req: { url: URL }) => Response | Promise<Response>) {
    const endpoint = capture('get', `Media/discover/${what}`, respond);
    server.use(endpoint.handler);
    return endpoint;
}

// The filter selects have no accessible name (InputLabel without labelId), so they are found by the value they show
const select = (shownValue: string) => {
    const found = screen.getAllByRole('combobox').find(c => c.textContent === shownValue);
    if (!found) throw new Error(`No select shows "${shownValue}"`);
    return found;
};

describe('DiscoverMusicPage', () => {
    it('discovers pop in the US by default', async () => {
        serveStats();
        const discover = serve('music', () => HttpResponse.json(paged([album({ title: 'Discovery' })])));
        renderWithApp(<DiscoverMusicPage />, { route: '/discover/music' });

        expect(screen.getByRole('heading', { name: 'Discover Music' })).toBeInTheDocument();
        expect(await screen.findByText('Discovery')).toBeInTheDocument();
        expect(Object.fromEntries(discover.last.url.searchParams)).toEqual({ page: '1', genre: 'pop', market: 'US' });
    });

    it('splits a year range from the URL into the two fields', async () => {
        serveStats();
        const discover = serve('music', () => HttpResponse.json(paged([])));
        renderWithApp(<DiscoverMusicPage />, { route: '/discover/music?genre=jazz&year=1950-1969&market=HU&page=2' });

        await screen.findByText('No results found for these filters.');
        expect(Object.fromEntries(discover.last.url.searchParams)).toEqual({ page: '2', genre: 'jazz', year: '1950-1969', market: 'HU' });
        expect(screen.getByLabelText('From (year)')).toHaveValue(1950);
        expect(screen.getByLabelText('To (year)')).toHaveValue(1969);
        expect(select('Jazz')).toBeInTheDocument();
        expect(select('HU')).toBeInTheDocument();
    });

    it.each([
        ['both years', '1990', '1999', 'year=1990-1999&'],
        ['only the start', '1990', '', 'year=1990&'],
        ['only the end', '', '1999', 'year=1999&'],
        ['no year', '', '', ''],
    ])('applies %s', async (_, from, to, yearQuery) => {
        serveStats();
        serve('music', () => HttpResponse.json(paged([])));
        const { user } = renderWithApp(<DiscoverMusicPage />, { route: '/discover/music' });
        await screen.findByText('No results found for these filters.');

        await user.click(select('Pop'));
        await user.click(screen.getByRole('option', { name: 'K-pop' }));
        await user.click(select('US'));
        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'KR' }));
        if (from) await user.type(screen.getByLabelText('From (year)'), from);
        if (to) await user.type(screen.getByLabelText('To (year)'), to);
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(currentLocation()).toBe(`/discover/music?genre=k-pop&${yearQuery}market=KR&page=1`);
    });

    it('clears back to the defaults', async () => {
        serveStats();
        serve('music', () => HttpResponse.json(paged([])));
        const { user } = renderWithApp(<DiscoverMusicPage />, { route: '/discover/music?genre=jazz&year=1950&market=HU' });
        await screen.findByText('No results found for these filters.');

        await user.click(screen.getByRole('button', { name: 'Clear' }));

        expect(currentLocation()).toBe('/discover/music');
        expect(select('Pop')).toBeInTheDocument();
        expect(screen.getByLabelText('From (year)')).toHaveValue(null);
    });

    it('pages through the results', async () => {
        serveStats();
        serve('music', req => HttpResponse.json(paged([album({ title: `Page ${req.url.searchParams.get('page')}` })], { totalPages: 2 })));
        const { user } = renderWithApp(<DiscoverMusicPage />, { route: '/discover/music?genre=rock' });
        await screen.findByText('Page 1');

        await user.click(screen.getAllByRole('button', { name: 'Go to page 2' })[0]);

        expect(await screen.findByText('Page 2')).toBeInTheDocument();
        expect(currentLocation()).toBe('/discover/music?genre=rock&page=2');
    });

    it('shows the error', async () => {
        serve('music', () => apiError(503, 'Spotify is not available.'));
        renderWithApp(<DiscoverMusicPage />, { route: '/discover/music' });

        expect(await screen.findByText('Spotify is not available.')).toBeInTheDocument();
    });
});
