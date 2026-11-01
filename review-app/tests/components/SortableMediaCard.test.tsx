import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/preact';
import { DndContext } from '@dnd-kit/core';
import { SortableContext } from '@dnd-kit/sortable';
import { SortableMediaCard } from '../../src/components/SortableMediaCard';
import { authUser, movie } from '../support/builders';
import { renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';

describe('SortableMediaCard', () => {
    function renderSortable(isOwner: boolean) {
        serveStats();
        const arrival = movie({ id: 'm1', title: 'Arrival' });
        renderWithApp(
            <DndContext>
                <SortableContext items={['m1']}>
                    <SortableMediaCard media={arrival} collectionId={5} isOwner={isOwner} onRemove={vi.fn()} />
                </SortableContext>
            </DndContext>,
            { user: authUser() },
        );
        return screen.getByText('Arrival').closest('[role="button"]') as HTMLElement;
    }

    it('is a grabbable, keyboard-reachable item for the owner', async () => {
        const handle = renderSortable(true);

        expect(handle).toHaveStyle({ cursor: 'grab' });
        expect(handle).toHaveAttribute('aria-roledescription', 'sortable');
        expect(handle).toHaveAttribute('tabindex', '0');
        expect(handle).not.toHaveAttribute('aria-disabled', 'true');
        await screen.findByRole('button', { name: 'Remove from Collection' });
    });

    it('can\'t be dragged by a visitor', async () => {
        const handle = renderSortable(false);

        expect(handle).toHaveStyle({ cursor: 'default' });
        expect(handle).toHaveAttribute('aria-disabled', 'true');
        await screen.findByRole('button', { name: 'Add to Collection' });
    });
});
