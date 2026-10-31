import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { SearchBar } from '../../src/components/SearchBar';
import { currentLocation, renderWithApp } from '../support/render';

describe('SearchBar', () => {
    it('searches all media with the escaped query, from page 1', async () => {
        const { user } = renderWithApp(<SearchBar placeholder="Search movies..." />);

        await user.type(screen.getByPlaceholderText('Search movies...'), 'star wars & co');
        await user.click(screen.getByRole('button', { name: 'Search' }));

        expect(currentLocation()).toBe('/search?q=star%20wars%20%26%20co&type=all&page=1');
    });

    it('submits with Enter', async () => {
        const { user } = renderWithApp(<SearchBar defaultType="series" />);

        await user.type(screen.getByPlaceholderText('Search...'), 'lost{Enter}');

        expect(currentLocation()).toBe('/search?q=lost&type=series&page=1');
    });

    it('ignores an empty or blank query', async () => {
        const { user } = renderWithApp(<SearchBar />, { route: '/home' });

        await user.click(screen.getByRole('button', { name: 'Search' }));
        await user.type(screen.getByPlaceholderText('Search...'), '   {Enter}');

        expect(currentLocation()).toBe('/home');
    });

    it('starts with the given query', () => {
        renderWithApp(<SearchBar defaultQuery="dune" />);
        expect(screen.getByPlaceholderText('Search...')).toHaveValue('dune');
    });

    it('lets the user pick the media type', async () => {
        const { user } = renderWithApp(<SearchBar showTypeSelect defaultQuery="x" />);

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Music' }));
        await user.click(screen.getByRole('button', { name: 'Search' }));

        expect(currentLocation()).toBe('/search?q=x&type=music&page=1');
    });

    it('has no type picker unless asked', () => {
        renderWithApp(<SearchBar />);
        expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });
});
