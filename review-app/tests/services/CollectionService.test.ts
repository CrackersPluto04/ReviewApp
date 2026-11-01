import { describe, expect } from 'vitest';
import { collectionService } from '../../src/services/CollectionService';
import { ok, noContent } from '../support/api';
import { describeEndpoint } from './endpoint';

const collectionWithMedia = { collection: { id: 5, name: 'Sci-fi' }, mediaItems: [] };

describe('CollectionService', () => {
    describeEndpoint({
        name: 'getCollectionWithMedias',
        method: 'get',
        path: 'Collection/5',
        call: () => collectionService.getCollectionWithMedias(5),
        response: ok(collectionWithMedia),
        result: { success: true, data: collectionWithMedia },
        fallback: 'Something went wrong while getting collection items.',
        network: 'Network error while getting collection items.',
    });

    describeEndpoint({
        name: 'createCollection',
        method: 'post',
        path: 'Collection',
        call: () => collectionService.createCollection('Sci-fi', 1),
        request: req => expect(req.body).toEqual({ name: 'Sci-fi', visibilityLevel: 1 }),
        response: ok({ id: 9, name: 'Sci-fi' }),
        result: { success: true, data: { id: 9, name: 'Sci-fi' } },
        fallback: 'Something went wrong while creating collection.',
        network: 'Network error while creating collection.',
    });

    describeEndpoint({
        name: 'createCollection (default visibility)',
        method: 'post',
        path: 'Collection',
        call: () => collectionService.createCollection('Mine'),
        // Private unless chosen otherwise
        request: req => expect(req.body).toEqual({ name: 'Mine', visibilityLevel: 0 }),
        response: ok({ id: 1 }),
        result: { success: true, data: { id: 1 } },
        fallback: 'Something went wrong while creating collection.',
        network: 'Network error while creating collection.',
    });

    describeEndpoint({
        name: 'updateCollection',
        method: 'put',
        path: 'Collection',
        call: () => collectionService.updateCollection(5, 'Renamed', 2),
        request: req => expect(req.body).toEqual({ collectionID: 5, name: 'Renamed', visibilityLevel: 2 }),
        response: ok({ id: 5, name: 'Renamed' }),
        result: { success: true, data: { id: 5, name: 'Renamed' } },
        fallback: 'Something went wrong while updating collection.',
        network: 'Network error while updating collection.',
    });

    describeEndpoint({
        name: 'deleteCollection',
        method: 'delete',
        path: 'Collection/5',
        call: () => collectionService.deleteCollection(5),
        response: noContent(),
        result: { success: true, data: null },
        fallback: 'Something went wrong while deleting collection.',
        network: 'Network error while deleting collection.',
    });

    describeEndpoint({
        name: 'addMediaToCollection',
        method: 'post',
        path: 'Collection/5/media',
        call: () => collectionService.addMediaToCollection(5, 2, 'spotify-1'),
        request: req => expect(req.body).toEqual({ type: 2, externalApiID: 'spotify-1' }),
        response: ok({ message: 'Added' }),
        result: { success: true, data: { message: 'Added' } },
        fallback: 'Something went wrong while adding media to collection.',
        network: 'Network error while adding media to collection.',
    });

    describeEndpoint({
        name: 'removeMediaFromCollection',
        method: 'delete',
        // The media key is "<type>-<external id>"
        path: 'Collection/5/media/1-tmdb-42',
        call: () => collectionService.removeMediaFromCollection(5, 1, 'tmdb-42'),
        response: noContent(),
        result: { success: true, data: null },
        fallback: 'Something went wrong while removing media from collection.',
        network: 'Network error while removing media from collection.',
    });

    describeEndpoint({
        name: 'reorderMedia',
        method: 'put',
        path: 'Collection/5/reorder',
        call: () => collectionService.reorderMedia(5, 77, 3),
        request: req => expect(req.body).toEqual({ dbMediaID: 77, newOrderIndex: 3 }),
        response: ok({ message: 'Reordered' }),
        result: { success: true, data: { message: 'Reordered' } },
        fallback: 'Something went wrong while reordering media in collection.',
        network: 'Network error while reordering media in collection.',
    });
});
