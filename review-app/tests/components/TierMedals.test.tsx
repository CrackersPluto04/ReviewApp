import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { TierMedals } from '../../src/components/TierMedals';
import { tier } from '../support/builders';

describe('TierMedals', () => {
    it('shows one medal per tier with its state', () => {
        render(<TierMedals tiers={[tier({ tier: 'Bronze', isUnlocked: true }), tier({ tier: 'Silver' })]} />);

        expect(screen.getByLabelText('Bronze earned')).toBeInTheDocument();
        expect(screen.getByLabelText('Silver not earned')).toBeInTheDocument();
        expect(screen.queryByLabelText(/Gold/)).not.toBeInTheDocument();
    });
});
