import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { Footer } from '../../src/components/Footer';
import { useFakeTimers } from '../support/render';

describe('Footer', () => {
    it('shows the current year', () => {
        useFakeTimers('2031-06-15T12:00:00Z');
        render(<Footer />);

        expect(screen.getByText('© 2031 THE Review App. All rights reserved.')).toBeInTheDocument();
    });
});
