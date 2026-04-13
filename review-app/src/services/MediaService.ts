class MediaService {
    private readonly baseUrl = 'https://localhost:7140/api/Media';

    async searchAll(query: string) {
        return this.search(query, 'all');
    }

    async searchMovies(query: string) {
        return this.search(query, 'movie');
    }

    async searchSeries(query: string) {
        return this.search(query, 'series');
    }

    async searchMusic(query: string) {
        return this.search(query, 'music');
    }

    // Private helper to handle the actual search logic
    private async search(query: string, what: 'all' | 'movie' | 'series' | 'music') {
        try {
            const response = await fetch(`${this.baseUrl}/search/${what}?query=${encodeURIComponent(query)}`);

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