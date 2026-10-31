import { describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import type { DragEndEvent } from '@dnd-kit/core';
import { CollectionDetails } from '../../src/components/CollectionDetails';
import { server } from '../support/server';
import { apiError, capture, ok } from '../support/api';
import { authUser, collection, collectionMedia, movie } from '../support/builders';
import { renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';
import { knownIssue } from '../support/known-issue';

/**
 * jsdom has no layout, so a real drag (pointer or keyboard) can't find drop targets. The sensors and the sortable
 * attributes are covered with the real dnd-kit in CollectionDetails.test.tsx; here DndContext is replaced to capture
 * its onDragEnd, which is called with the event dnd-kit would produce for a drop.
 */
let dropHandler: ((event: DragEndEvent) => void) | undefined;

vi.mock('@dnd-kit/core', async importOriginal => {
    const actual = await importOriginal<typeof import('@dnd-kit/core')>();
    return {
        ...actual,
        DndContext: ({ children, onDragEnd }: { children: any; onDragEnd: (event: DragEndEvent) => void }) => {
            dropHandler = onDragEnd;
            return children;
        },
    };
});

const drop = (activeId: string, overId: string | null) =>
    act(() => dropHandler!({ active: { id: activeId }, over: overId === null ? null : { id: overId } } as unknown as DragEndEvent));

const titles = () => screen.getAllByRole('heading', { level: 5 }).map(h => h.textContent);

async function renderCollection() {
    serveStats();
    const items = ['Arrival', 'Contact', 'Dune'].map((title, i) =>
        collectionMedia({ dbMediaID: 100 + i, orderIndex: i, media: movie({ id: `m${i}`, title }) }));
    server.use(capture('get', 'Collection/5', HttpResponse.json({ collection: collection({ id: 5, name: 'Sci-fi', isOwner: true }), mediaItems: items })).handler);
    renderWithApp(<CollectionDetails collectionId={5} onCollectionEdited={vi.fn()} onCollectionDeleted={vi.fn()} onMediaRemoved={vi.fn()} />, { user: authUser() });
    await screen.findByText('Dune');
}

describe('CollectionDetails drag and drop', () => {
    it('moves the media at once and saves its new index', async () => {
        const reorder = capture('put', 'Collection/5/reorder', ok({ message: 'Reordered' }));
        server.use(reorder.handler);
        await renderCollection();

        await drop('m0', 'm2');

        expect(titles()).toEqual(['Contact', 'Dune', 'Arrival']);
        await waitFor(() => expect(reorder.calls).toHaveLength(1));
        expect(reorder.last.body).toEqual({ dbMediaID: 100, newOrderIndex: 2 });
    });

    it('moves upwards too', async () => {
        const reorder = capture('put', 'Collection/5/reorder', ok({}));
        server.use(reorder.handler);
        await renderCollection();

        await drop('m2', 'm0');

        expect(titles()).toEqual(['Dune', 'Arrival', 'Contact']);
        await waitFor(() => expect(reorder.last.body).toEqual({ dbMediaID: 102, newOrderIndex: 0 }));
    });

    it.each([
        ['outside the list', null],
        ['on its own place', 'm1'],
    ])('does nothing for a drop %s', async (_, over) => {
        const reorder = capture('put', 'Collection/5/reorder', ok({}));
        server.use(reorder.handler);
        await renderCollection();

        await drop('m1', over);

        expect(titles()).toEqual(['Arrival', 'Contact', 'Dune']);
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(reorder.calls).toHaveLength(0);
    });

    knownIssue('W14', 'a failed reorder puts the media back and says so', async () => {
        server.use(capture('put', 'Collection/5/reorder', apiError(500, 'Could not save the order.')).handler);
        await renderCollection();

        await drop('m0', 'm2');

        expect(await screen.findByText('Could not save the order.')).toBeInTheDocument();
        expect(titles()).toEqual(['Arrival', 'Contact', 'Dune']);
    });
});
