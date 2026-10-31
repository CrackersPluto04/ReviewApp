import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { Footer } from '../../src/components/Footer';
import { PageContainer } from '../../src/components/PageContainer';
import { ScrollToTop } from '../../src/components/ScrollToTop';
import { PleaseLogin } from '../../src/components/PleaseLogin';
import { PremiumBadge } from '../../src/components/PremiumBadge';
import { PremiumUpsell } from '../../src/components/PremiumUpsell';
import { TierMedals } from '../../src/components/TierMedals';
import { MyPagination } from '../../src/components/MyPagination';
import { authUser, tier } from '../support/builders';
import { currentLocation, currentLocationState, renderWithApp, setupUser, useFakeTimers } from '../support/render';

describe('Footer', () => {
    it('shows the current year', () => {
        useFakeTimers('2031-06-15T12:00:00Z');
        render(<Footer />);

        expect(screen.getByText('© 2031 THE Review App. All rights reserved.')).toBeInTheDocument();
    });
});

describe('PageContainer', () => {
    it('wraps the page in the header, the verification banner and the footer', async () => {
        renderWithApp(<PageContainer mode="light" toggleTheme={() => {}}><p>The page</p></PageContainer>, {
            user: authUser({ isEmailConfirmed: false }),
        });

        expect(screen.getByRole('banner')).toHaveTextContent('THE Review App');
        expect(screen.getByRole('main')).toHaveTextContent('The page');
        expect(await screen.findByText(/Please verify your email address/)).toBeInTheDocument();
        expect(screen.getByText(/All rights reserved/)).toBeInTheDocument();
    });
});

describe('ScrollToTop', () => {
    function Nav() {
        const navigate = useNavigate();
        return <>
            <button onClick={() => navigate('/b')}>to b</button>
            <button onClick={() => navigate('/b?page=2')}>to b page 2</button>
        </>;
    }

    it('scrolls to the top when the path changes, not on query changes', async () => {
        const scrollTo = vi.mocked(window.scrollTo);
        scrollTo.mockClear();
        const user = setupUser();
        render(<MemoryRouter initialEntries={['/a']}><ScrollToTop /><Routes><Route path="*" element={<Nav />} /></Routes></MemoryRouter>);
        expect(scrollTo).toHaveBeenCalledTimes(1);

        // Preact runs effects after the next paint
        await user.click(screen.getByRole('button', { name: 'to b' }));
        await waitFor(() => expect(scrollTo).toHaveBeenCalledTimes(2));
        expect(scrollTo).toHaveBeenLastCalledWith(0, 0);

        await user.click(screen.getByRole('button', { name: 'to b page 2' }));
        await new Promise(resolve => setTimeout(resolve, 100));
        expect(scrollTo).toHaveBeenCalledTimes(2);
    });
});

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

describe('PremiumBadge', () => {
    it('is a labelled Premium chip', () => {
        render(<PremiumBadge />);
        expect(screen.getByLabelText('Premium member')).toHaveTextContent('Premium');
    });
});

describe('PremiumUpsell', () => {
    it('links a locked feature to the plans page', async () => {
        const { user } = renderWithApp(<PremiumUpsell />);

        expect(screen.getByText('This is a Premium feature')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'See Premium' }));

        expect(currentLocation()).toBe('/premium');
    });

    it('takes its own title and message', () => {
        renderWithApp(<PremiumUpsell title="Statistics" message="Charts of your reviews." />);

        expect(screen.getByText('Statistics')).toBeInTheDocument();
        expect(screen.getByText('Charts of your reviews.')).toBeInTheDocument();
    });
});

describe('TierMedals', () => {
    it('shows one medal per tier with its state', () => {
        render(<TierMedals tiers={[tier({ tier: 'Bronze', isUnlocked: true }), tier({ tier: 'Silver' })]} />);

        expect(screen.getByLabelText('Bronze earned')).toBeInTheDocument();
        expect(screen.getByLabelText('Silver not earned')).toBeInTheDocument();
        expect(screen.queryByLabelText(/Gold/)).not.toBeInTheDocument();
    });
});

describe('MyPagination', () => {
    it('reports the chosen page', async () => {
        const onPageChange = vi.fn();
        const user = setupUser();
        render(<MyPagination page={2} totalPages={5} onPageChange={onPageChange} size="medium" />);

        expect(screen.getByRole('button', { name: 'page 2' })).toHaveAttribute('aria-current', 'page');
        await user.click(screen.getByRole('button', { name: 'Go to page 4' }));
        await user.click(screen.getByRole('button', { name: 'Go to last page' }));

        expect(onPageChange.mock.calls.map(c => c[1])).toEqual([4, 5]);
    });

    it('stops at page 500 (TMDb\'s limit)', () => {
        render(<MyPagination page={1} totalPages={9000} onPageChange={() => {}} size="small" />);

        expect(screen.getByRole('button', { name: 'Go to page 500' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Go to page 501' })).not.toBeInTheDocument();
    });
});
