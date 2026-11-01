import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/preact';
import { AchievementFilterBar } from '../../src/components/AchievementFilterBar';
import { setupUser } from '../support/render';

const chip = (name: string) => screen.getByRole('button', { name });

describe('AchievementFilterBar', () => {
    function renderBar(props: Partial<Parameters<typeof AchievementFilterBar>[0]> = {}) {
        const handlers = { onToggleStatus: vi.fn(), onToggleCategory: vi.fn(), onSortChange: vi.fn(), onReset: vi.fn() };
        render(<AchievementFilterBar statuses={[]} categories={[]} sort="default" {...handlers} {...props} />);
        return { ...handlers, user: setupUser() };
    }

    it('has a chip for every status and category', () => {
        renderBar();

        for (const name of ['Unlocked', 'Locked', 'In progress', 'Movie', 'Series', 'Music', 'Review', 'Reply', 'Collection'])
            expect(chip(name)).toHaveAttribute('aria-pressed', 'false');
    });

    it('reports toggles and sort changes', async () => {
        const { user, onToggleStatus, onToggleCategory } = renderBar();

        await user.click(chip('In progress'));
        await user.click(chip('Series'));

        expect(onToggleStatus).toHaveBeenCalledWith('in-progress');
        expect(onToggleCategory).toHaveBeenCalledWith('Series');
    });

    it('enables reset only with an active filter', async () => {
        renderBar();
        expect(screen.getByRole('button', { name: 'Reset' })).toBeDisabled();
    });

    it.each([
        [{ statuses: ['locked'] as any }],
        [{ categories: ['Music'] }],
        [{ sort: 'title' as const }],
    ])('can reset %j', async props => {
        const { user, onReset } = renderBar(props);

        await user.click(screen.getByRole('button', { name: 'Reset' }));

        expect(onReset).toHaveBeenCalled();
    });
});
