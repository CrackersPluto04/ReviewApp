import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { ReviewFilter } from '../../src/components/ReviewFilter';
import { setupUser } from '../support/render';

const defaults = { sortBy: 'created_desc', scoreRange: [1, 10], hasWrittenText: false };

function renderFilter(props: Partial<Parameters<typeof ReviewFilter>[0]> = {}) {
    const handlers = { handleApply: vi.fn(), handleClear: vi.fn() };
    render(<ReviewFilter filters={defaults} disabled={false} {...handlers} {...props} />);
    return { ...handlers, user: setupUser() };
}

describe('ReviewFilter', () => {
    it('starts from the given filters', () => {
        renderFilter({ filters: { sortBy: 'score_asc', scoreRange: [2.5, 8], hasWrittenText: true } });

        expect(screen.getByRole('combobox')).toHaveTextContent('Lowest Score');
        expect(screen.getByText('Score Range: 2.5 - 8')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).toBeChecked();
    });

    it('applies the drafted filters only on Apply', async () => {
        const { user, handleApply } = renderFilter();

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Recently Updated' }));
        const [, high] = screen.getAllByRole('slider');
        high.focus();
        await user.keyboard('{ArrowLeft}');
        await user.click(screen.getByRole('switch', { name: 'Only show written reviews' }));
        expect(handleApply).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(handleApply).toHaveBeenCalledWith({ sortBy: 'updated_desc', scoreRange: [1, 9.5], hasWrittenText: true });
    });

    it('offers every sort', async () => {
        const { user } = renderFilter();

        await user.click(screen.getByRole('combobox'));

        expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual([
            'Newest First', 'Oldest First', 'Recently Updated', 'Least Recently Updated', 'Highest Score', 'Lowest Score',
        ]);
    });

    it('clears back to the defaults', async () => {
        const { user, handleClear } = renderFilter({ filters: { sortBy: 'score_asc', scoreRange: [3, 7], hasWrittenText: true } });

        await user.click(screen.getByRole('button', { name: 'Clear' }));

        expect(handleClear).toHaveBeenCalled();
        expect(screen.getByText('Score Range: 1 - 10')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Only show written reviews' })).not.toBeChecked();
        expect(screen.getByRole('combobox')).toHaveTextContent('Newest First');
    });

    it('disables both buttons when told to', () => {
        renderFilter({ disabled: true });

        expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Clear' })).toBeDisabled();
    });
});
