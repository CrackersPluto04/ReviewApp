class MediaService {
    async searchMovies(query: string) {
        try {
            const response = await fetch(`https://localhost:7140/api/Media/search?query=${encodeURIComponent(query)}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Search failed' };
            }
        } catch (error) {
            console.error('Search error:', error);
            return { success: false, message: 'Network error while searching.' };
        }
    }
}

export const mediaService = new MediaService();