import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { MyPagination } from '../../src/components/MyPagination';
import { setupUser } from '../support/render';

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
