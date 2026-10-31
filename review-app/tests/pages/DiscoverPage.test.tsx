import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { useNavigate } from 'react-router-dom';
import { DiscoverPage } from '../../src/pages/DiscoverPage';
import { DiscoverMusicPage } from '../../src/pages/DiscoverMusicPage';
import { server } from '../support/server';
import { apiError, capture, deferred } from '../support/api';
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

describe('DiscoverPage', () => {
    it('discovers popular movies by default', async () => {
        serveStats();
        const discover = serve('movie', () => HttpResponse.json(paged([movie({ title: 'Arrival' })])));
        renderWithApp(<DiscoverPage />, { route: '/discover' });

        expect(screen.getByRole('heading', { name: 'Discover Movies' })).toBeInTheDocument();
        expect(await screen.findByText('Arrival')).toBeInTheDocument();
        expect(Object.fromEntries(discover.last.url.searchParams)).toEqual({ page: '1', sortBy: 'popularity.desc' });
    });

    it('reads every filter from the URL', async () => {
        serveStats();
        const discover = serve('series', () => HttpResponse.json(paged([series({ title: 'Lost' })])));
        renderWithApp(<DiscoverPage />, { route: '/discover?type=series&page=2&sortBy=name.asc&year=2004&withGenres=18&minRuntime=30&maxRuntime=60' });

        expect(await screen.findByText('Lost')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Discover Series' })).toBeInTheDocument();
        expect(Object.fromEntries(discover.last.url.searchParams)).toEqual({
            page: '2', sortBy: 'name.asc', year: '2004', withGenres: '18', minRuntime: '30', maxRuntime: '60',
        });
        expect(screen.getByLabelText('Year')).toHaveValue(2004);
        expect(select('A-Z')).toBeInTheDocument();
        expect(select('Drama')).toBeInTheDocument();
    });

    it('applies the chosen filters from page 1', async () => {
        serveStats();
        const discover = serve('movie', () => HttpResponse.json(paged([])));
        const { user } = renderWithApp(<DiscoverPage />, { route: '/discover?page=4' });
        await screen.findByText('No results found for these filters.');

        await user.click(select('Most Popular'));
        await user.click(screen.getByRole('option', { name: 'Newest Releases' }));
        await user.click(screen.getAllByRole('combobox')[2]);
        await user.click(screen.getByRole('option', { name: 'Sci-Fi' }));
        await user.type(screen.getByLabelText('Year'), '1999');
        await user.type(screen.getByLabelText('Min Runtime'), '90');
        // Nothing is fetched before Apply
        expect(discover.calls).toHaveLength(1);
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(currentLocation()).toBe('/discover?type=movie&sortBy=primary_release_date.desc&year=1999&withGenres=878&minRuntime=90&page=1');
        await waitFor(() => expect(discover.calls).toHaveLength(2));
    });

    it('switches to series with their own sort values and genres', async () => {
        serveStats();
        serve('movie', () => HttpResponse.json(paged([])));
        const seriesCall = serve('series', () => HttpResponse.json(paged([])));
        const { user } = renderWithApp(<DiscoverPage />, { route: '/discover' });
        await screen.findByText('No results found for these filters.');

        await user.click(select('Movies'));
        await user.click(screen.getByRole('option', { name: 'Series' }));
        await user.click(select('Most Popular'));
        await user.click(screen.getByRole('option', { name: 'Z-A' }));
        await user.click(screen.getAllByRole('combobox')[2]);
        expect(screen.queryByRole('option', { name: 'Sci-Fi' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('option', { name: 'Sci-Fi & Fantasy' }));
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(currentLocation()).toBe('/discover?type=series&sortBy=name.desc&withGenres=10765&page=1');
        expect(await screen.findByRole('heading', { name: 'Discover Series' })).toBeInTheDocument();
        await waitFor(() => expect(seriesCall.calls).toHaveLength(1));
    });

    it('clears the filters but keeps the type', async () => {
        serveStats();
        serve('series', () => HttpResponse.json(paged([])));
        const { user } = renderWithApp(<DiscoverPage />, { route: '/discover?type=series&year=2004&withGenres=18&page=3' });
        await screen.findByText('No results found for these filters.');

        await user.click(screen.getByRole('button', { name: 'Clear' }));

        expect(currentLocation()).toBe('/discover?type=series');
        expect(screen.getByLabelText('Year')).toHaveValue(null);
    });

    it('pages through the results, keeping the filters', async () => {
        serveStats();
        serve('movie', req => HttpResponse.json(paged([movie({ title: `Page ${req.url.searchParams.get('page')}` })], { totalPages: 3 })));
        const { user } = renderWithApp(<DiscoverPage />, { route: '/discover?type=movie&year=1999' });
        await screen.findByText('Page 1');

        await user.click(screen.getAllByRole('button', { name: 'Go to page 3' })[0]);

        expect(await screen.findByText('Page 3')).toBeInTheDocument();
        expect(currentLocation()).toBe('/discover?type=movie&year=1999&page=3');
    });

    it('shows a spinner, then the error', async () => {
        const response = deferred(() => apiError(503, 'TMDb is not available.'));
        serve('movie', () => response.promise);
        renderWithApp(<DiscoverPage />, { route: '/discover' });

        expect(await screen.findByRole('progressbar')).toBeInTheDocument();
        response.release();
        expect(await screen.findByText('TMDb is not available.')).toBeInTheDocument();
    });

    it('follows the URL when it changes (e.g. the header links)', async () => {
        function SeriesLink() {
            const navigate = useNavigate();
            return <button onClick={() => navigate('/discover?type=series')}>Series link</button>;
        }
        serveStats();
        serve('movie', () => HttpResponse.json(paged([])));
        serve('series', () => HttpResponse.json(paged([series({ title: 'Lost' })])));
        const { user } = renderWithApp(<><DiscoverPage /><SeriesLink /></>, { route: '/discover?type=movie' });
        await screen.findByText('No results found for these filters.');

        // Typed, but not applied: the navigation replaces the drafts with the URL's filters
        await user.type(screen.getByLabelText('Year'), '1990');
        await user.click(screen.getByRole('button', { name: 'Series link' }));

        expect(await screen.findByText('Lost')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Discover Series' })).toBeInTheDocument();
        await waitFor(() => expect(screen.getByLabelText('Year')).toHaveValue(null));
        expect(select('Series')).toBeInTheDocument();
    });
});

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
