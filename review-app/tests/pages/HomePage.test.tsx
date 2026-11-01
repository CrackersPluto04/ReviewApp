import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { HomePage } from '../../src/pages/HomePage';

import { currentLocation, renderWithApp } from '../support/render';

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
