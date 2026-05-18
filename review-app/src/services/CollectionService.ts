class CollectionService {
    private readonly baseUrl = 'https://localhost:7140/api/Collection';

    async getCollectionWithMedias(collectionId: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${collectionId}`, this.getFetchOptions());

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

    async createCollection(name: string, visibilityLevel: number = 0) {
        try {
            const response = await fetch(this.baseUrl, this.getFetchOptions('POST', { name, visibilityLevel }));

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

    async updateCollection(collectionId: number, name: string, visibilityLevel: number) {
        try {
            const response = await fetch(this.baseUrl, this.getFetchOptions('PUT', { collectionId, name, visibilityLevel }));

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

    async deleteCollection(collectionId: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${collectionId}`, this.getFetchOptions('DELETE'));

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

    async addMediaToCollection(collectionId: number, mediaType: number, externalApiId: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${collectionId}/media`, this.getFetchOptions('POST', { mediaType, externalApiId }));

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

    async removeMediaFromCollection(collectionId: number, mediaType: number, externalApiId: string) {
        try {
            const response = await fetch(`${this.baseUrl}/${collectionId}/media/${mediaType}-${externalApiId}`, this.getFetchOptions('DELETE'));

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

    async reorderMedia(collectionId: number, dbMediaID: number, newOrderIndex: number) {
        try {
            const response = await fetch(`${this.baseUrl}/${collectionId}/reorder`, this.getFetchOptions('PUT', { dbMediaID, newOrderIndex }));

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

    /* Helper methods */

    // Create fetch options for different HTTP methods and request bodies
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