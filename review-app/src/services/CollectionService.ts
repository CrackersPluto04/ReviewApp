import { apiFetch } from "./apiClient";

/**
 * Wraps `api/Collection`. Reading is open to anonymous visitors; every change needs a login and works on the
 * caller's own collections only. The default Favourites collection is permanent: the API refuses to rename,
 * edit or delete it.
 * Methods never throw; they resolve to `{ success, data }` or `{ success: false, message }`.
 */
class CollectionService {
    private readonly baseUrl = 'https://localhost:7140/api/Collection';

    /**
     * Opens a collection with its media in the saved order. Visibility is decided per viewer.
     * @param collectionId The collection's ID.
     * @returns `{ success: true, data }`, or a failure; a private or missing collection fails the same way (404).
     */
    async getCollectionWithMedias(collectionId: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${collectionId}`, this.getFetchOptions());

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting collection items.' };
            }
        } catch (error) {
            console.error('GetCollectionWithMedias error:', error);
            return { success: false, message: 'Network error while getting collection items.' };
        }
    }

    /**
     * Creates a collection. Names are unique per user.
     * @param name The collection's name.
     * @param visibilityLevel The `VisibilityLevel` value (0 = private, 1 = public, 2 = followers only); private unless given.
     * @returns `{ success: true, data }` with the created collection, or a failure (e.g. the name is taken).
     */
    async createCollection(name: string, visibilityLevel: number = 0) {
        try {
            const response = await apiFetch(this.baseUrl, this.getFetchOptions('POST', { name, visibilityLevel }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while creating collection.' };
            }
        } catch (error) {
            console.error('CreateCollection error:', error);
            return { success: false, message: 'Network error while creating collection.' };
        }
    }

    /**
     * Changes a collection's name and visibility.
     * @param collectionID The collection's ID.
     * @param name The new name.
     * @param visibilityLevel The new `VisibilityLevel` value.
     * @returns `{ success: true, data }` with the updated collection, or a failure (empty or taken name, Favourites).
     */
    async updateCollection(collectionID: number, name: string, visibilityLevel: number) {
        try {
            const response = await apiFetch(this.baseUrl, this.getFetchOptions('PUT', { collectionID, name, visibilityLevel }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while updating collection.' };
            }
        } catch (error) {
            console.error('UpdateCollection error:', error);
            return { success: false, message: 'Network error while updating collection.' };
        }
    }

    /**
     * Deletes a collection and its item links; the media themselves stay.
     * @param collectionId The collection's ID.
     * @returns `{ success: true, data: null }`, or a failure (e.g. it is the Favourites collection).
     */
    async deleteCollection(collectionId: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${collectionId}`, this.getFetchOptions('DELETE'));

            if (response.ok) {
                return { success: true, data: null };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while deleting collection.' };
            }
        } catch (error) {
            console.error('DeleteCollection error:', error);
            return { success: false, message: 'Network error while deleting collection.' };
        }
    }

    /**
     * Adds a title. The API fetches it from TMDb / Spotify the first time anyone uses it, so an unknown title or
     * an outage of the source fails here (`media_not_found`, `source_unavailable`).
     * @param collectionId The collection's ID.
     * @param type The `MediaType` value (0 = movie, 1 = series, 2 = music).
     * @param externalApiID The title's TMDb or Spotify ID.
     * @returns `{ success: true, data }`, or a failure (e.g. it is already in the collection).
     */
    async addMediaToCollection(collectionId: number, type: number, externalApiID: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${collectionId}/media`, this.getFetchOptions('POST', { type, externalApiID }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while adding media to collection.' };
            }
        } catch (error) {
            console.error('AddMediaToCollection error:', error);
            return { success: false, message: 'Network error while adding media to collection.' };
        }
    }

    /**
     * Removes a title from a collection. The title is addressed as `{mediaType}-{externalApiId}` in the route.
     * @param collectionId The collection's ID.
     * @param mediaType The `MediaType` value.
     * @param externalApiId The title's TMDb or Spotify ID.
     * @returns `{ success: true, data: null }`, or a failure (e.g. the title is not in the collection).
     */
    async removeMediaFromCollection(collectionId: number, mediaType: number, externalApiId: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${collectionId}/media/${mediaType}-${externalApiId}`, this.getFetchOptions('DELETE'));

            if (response.ok) {
                return { success: true, data: null };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while removing media from collection.' };
            }
        } catch (error) {
            console.error('RemoveMediaFromCollection error:', error);
            return { success: false, message: 'Network error while removing media from collection.' };
        }
    }

    /**
     * Moves one title to a new position, which is what a drag and drop produces.
     * @param collectionId The collection's ID.
     * @param dbMediaID The stored media ID (not the external API ID) of the title that moved.
     * @param newOrderIndex Its new position in the list.
     * @returns `{ success: true, data }`, or a failure (e.g. the title is not in the collection).
     */
    async reorderMedia(collectionId: number, dbMediaID: number, newOrderIndex: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${collectionId}/reorder`, this.getFetchOptions('PUT', { dbMediaID, newOrderIndex }));

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while reordering media in collection.' };
            }
        } catch (error) {
            console.error('ReorderMedia error:', error);
            return { success: false, message: 'Network error while reordering media in collection.' };
        }
    }

    private getFetchOptions(method: string = 'GET', body?: any): RequestInit {
        return {
            method,
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: body ? JSON.stringify(body) : undefined
        };
    }
}

export const collectionService = new CollectionService();
