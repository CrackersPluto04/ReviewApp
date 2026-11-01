import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { PleaseLogin } from '../../src/components/PleaseLogin';
import { currentLocation, currentLocationState, renderWithApp } from '../support/render';

describe('PleaseLogin', () => {
    it('shows the default message and sends the user to login with the way back', async () => {
        const { user } = renderWithApp(<PleaseLogin />, { route: '/profile/bob/collections' });

        expect(screen.getByRole('heading', { name: 'You must be logged in to view this.' })).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Log In or Register' }));

        expect(currentLocation()).toBe('/login');
        expect(currentLocationState().returnTo.pathname).toBe('/profile/bob/collections');
    });

    it('has a compact inline variant with its own message', async () => {
        const { user } = renderWithApp(<PleaseLogin compact message="Log in to reply." />);

        expect(screen.queryByRole('heading')).not.toBeInTheDocument();
        expect(screen.getByText('Log in to reply.')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Log In or Register' }));
        expect(currentLocation()).toBe('/login');
    });
});
