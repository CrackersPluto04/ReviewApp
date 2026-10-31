import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { PremiumUpsell } from '../../src/components/PremiumUpsell';
import { currentLocation, renderWithApp } from '../support/render';

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
