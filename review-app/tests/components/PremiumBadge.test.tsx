import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { PremiumBadge } from '../../src/components/PremiumBadge';

describe('PremiumBadge', () => {
    it('is a labelled Premium chip', () => {
        render(<PremiumBadge />);
        expect(screen.getByLabelText('Premium member')).toHaveTextContent('Premium');
    });
});
