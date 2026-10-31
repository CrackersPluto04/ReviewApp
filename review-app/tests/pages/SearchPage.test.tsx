import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { SearchPage } from '../../src/pages/SearchPage';
import { HomePage } from '../../src/pages/HomePage';
import { server } from '../support/server';
import { apiError, capture, deferred } from '../support/api';
import { movie, paged } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';
import { knownIssue } from '../support/known-issue';

function serve(what: string, respond: (req: { url: URL }) => Response | Promise<Response>) {
    const endpoint = capture('get', `Media/search/${what}`, respond);
    server.use(endpoint.handler);
    return endpoint;
}

const results = (titles: string[], overrides: Record<string, any> = {}) =>
    HttpResponse.json(paged(titles.map(title => movie({ title })), overrides));

describe('HomePage', () => {
    it('welcomes and searches with a type picker', async () => {
        const { user } = renderWithApp(<HomePage />);

        expect(screen.getByRole('heading', { name: /Welcome to THE Review App!/ })).toBeInTheDocument();
        await user.type(screen.getByPlaceholderText('Search for a piece of media...'), 'dune');
        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Movies' }));
        await user.click(screen.getByRole('button', { name: 'Search' }));

        expect(currentLocation()).toBe('/search?q=dune&type=movie&page=1');
    });
});

describe('SearchPage', () => {
    it('asks for a query when there is none', () => {
        renderWithApp(<SearchPage />, { route: '/search' });

        expect(screen.getByText('Type something above to start searching!')).toBeInTheDocument();
    });

    it('searches every media type for the query in the URL', async () => {
        serveStats();
        const all = serve('all', () => results(['Dune', 'Dune: Part Two'], { totalCount: 2 }));
        renderWithApp(<SearchPage />, { route: '/search?q=dune&type=all&page=1' });

        expect(await screen.findByText('Dune: Part Two')).toBeInTheDocument();
        expect(all.last.url.searchParams.get('query')).toBe('dune');
        expect(screen.getByRole('tab', { name: 'Any (2)' })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByPlaceholderText('Search...')).toHaveValue('dune');
    });

    it('searches one type on its page', async () => {
        serveStats();
        const movies = serve('movie', () => results(['Dune'], { totalCount: 31, totalPages: 4 }));
        renderWithApp(<SearchPage />, { route: '/search?q=dune&type=movie&page=3' });

        expect(await screen.findByText('Dune')).toBeInTheDocument();
        expect(movies.last.url.searchParams.get('page')).toBe('3');
        expect(screen.getByRole('tab', { name: 'Movies (31)' })).toHaveAttribute('aria-selected', 'true');
        // Pagination above and below the list
        expect(screen.getAllByRole('button', { name: 'page 3' })).toHaveLength(2);
    });

    it('switches type from the tabs, back to page 1', async () => {
        serveStats();
        serve('movie', () => results(['Dune']));
        serve('series', () => results(['Dune: Prophecy']));
        const { user } = renderWithApp(<SearchPage />, { route: '/search?q=dune&type=movie&page=2' });
        await screen.findByText('Dune');

        await user.click(screen.getByRole('tab', { name: /^Series/ }));

        expect(await screen.findByText('Dune: Prophecy')).toBeInTheDocument();
        expect(currentLocation()).toBe('/search?q=dune&type=series&page=1');
    });

    it('changes the page from the pagination', async () => {
        serveStats();
        serve('series', req => results([`Page ${req.url.searchParams.get('page')}`], { totalPages: 3 }));
        const { user } = renderWithApp(<SearchPage />, { route: '/search?q=x&type=series&page=1' });
        await screen.findByText('Page 1');

        await user.click(screen.getAllByRole('button', { name: 'Go to page 2' })[0]);

        expect(await screen.findByText('Page 2')).toBeInTheDocument();
        expect(currentLocation()).toBe('/search?q=x&type=series&page=2');
    });

    it('offers the per-type results below a mixed search', async () => {
        serveStats();
        serve('all', () => results(['Dune']));
        serve('music', () => HttpResponse.json({ items: [] }));
        const { user } = renderWithApp(<SearchPage />, { route: '/search?q=dune' });
        await screen.findByText('Dune');

        await user.click(screen.getByRole('button', { name: 'See all Music' }));

        expect(currentLocation()).toBe('/search?q=dune&type=music&page=1');
        expect(await screen.findByText('No results found. Try a different search.')).toBeInTheDocument();
    });

    it('disables the tabs while loading', async () => {
        const response = deferred(() => results([]));
        serve('all', () => response.promise);
        renderWithApp(<SearchPage />, { route: '/search?q=dune' });

        expect(await screen.findByRole('progressbar')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Music' })).toBeDisabled();
        response.release();
        expect(await screen.findByText('No results found. Try a different search.')).toBeInTheDocument();
    });

    it('shows the search error', async () => {
        serve('all', () => HttpResponse.json({ error: 'TMDb is not available.' }, { status: 503 }));
        renderWithApp(<SearchPage />, { route: '/search?q=dune' });

        expect(await screen.findByText('TMDb is not available.')).toBeInTheDocument();
    });

    knownIssue('W8', 'a successful search after a failed one no longer shows the error', async () => {
        serveStats();
        let fail = true;
        serve('movie', () => fail ? apiError(503, 'TMDb is not available.') : results([]));
        serve('series', () => results([]));
        const { user } = renderWithApp(<SearchPage />, { route: '/search?q=dune&type=movie' });
        await screen.findByText('TMDb is not available.');

        fail = false;
        await user.click(screen.getByRole('tab', { name: /^Series/ }));

        expect(await screen.findByText('No results found. Try a different search.')).toBeInTheDocument();
    });

    knownIssue('W8', 'a failed search shows its error instead of the previous results', async () => {
        serveStats();
        serve('movie', () => results(['Old result']));
        serve('series', () => apiError(503, 'TMDb is not available.'));
        const { user } = renderWithApp(<SearchPage />, { route: '/search?q=dune&type=movie' });
        await screen.findByText('Old result');

        await user.click(screen.getByRole('tab', { name: /^Series/ }));

        expect(await screen.findByText('TMDb is not available.')).toBeInTheDocument();
        expect(screen.queryByText('Old result')).not.toBeInTheDocument();
    });

    knownIssue('W8', 'the mixed search pages like the others', async () => {
        serveStats();
        const all = serve('all', () => results(['Dune'], { totalPages: 2 }));
        renderWithApp(<SearchPage />, { route: '/search?q=dune&type=all&page=2' });
        await screen.findByText('Dune');

        // The pagination is shown, but page 2 asks the API for page 1
        expect(all.last.url.searchParams.get('page')).toBe('2');
    });
});
