class MediaService {
    private readonly baseUrl = 'https://localhost:7140/api/Media';

    private spotifyBuffer = {
        query: '',
        uniqueTracks: [] as any[],
        currentOffset: 0,
        hasMore: true
    };

    async searchAll(query: string) {
        return this.search(query, 'all');
    }

    async searchMovies(query: string, page: number = 1) {
        return this.search(query, 'movie', page);
    }

    async searchSeries(query: string, page: number = 1) {
        return this.search(query, 'series', page);
    }

    async searchMusic(query: string, page: number = 1) {
        try {
            // 1. If the user typed a BRAND NEW search, wipe the buffer clean
            if (this.spotifyBuffer.query !== query) {
                this.spotifyBuffer = {
                    query: query,
                    uniqueTracks: [],
                    currentOffset: 0,
                    hasMore: true
                };
            }

            const pageSize = 20;
            const targetTrackCount = page * pageSize; // e.g., Page 2 needs 40 total tracks

            // 2. The "Just-In-Time" Loop: Keep fetching until we have enough tracks for this page
            while (this.spotifyBuffer.uniqueTracks.length < targetTrackCount && this.spotifyBuffer.hasMore) {
                // We have to hit the real C# endpoint, but we pass the OFFSET as the 'page' parameter 
                // Note: You'll need to slightly tweak C# to accept this, or just let C# do its normal math
                // Wait, simpler: We just call the C# endpoint asking for the exact Spotify limit pages.
                // Since C# expects 'page' (and translates it to offset), we just ask C# for page 1, 2, 3, etc.

                // Let's use a local C# page tracker based on offset
                const cSharpPageToRequest = (this.spotifyBuffer.currentOffset / 10) + 1;

                const response = await fetch(`${this.baseUrl}/search/music?query=${encodeURIComponent(query)}&page=${cSharpPageToRequest}`);

                if (!response.ok) break;

                const data = await response.json();
                const fetchedItems = data.items;

                // Break out if Spotify is completely empty
                if (fetchedItems.length === 0) {
                    this.spotifyBuffer.hasMore = false;
                    break;
                }

                // 3. Deduplicate! Only add tracks we haven't seen yet
                for (const item of fetchedItems) {
                    if (!this.spotifyBuffer.uniqueTracks.some(t => t.externalApiID === item.externalApiID)) {
                        this.spotifyBuffer.uniqueTracks.push(item);
                    }
                }

                // Increment our offset tracker by 10 (Spotify's new max limit)
                this.spotifyBuffer.currentOffset += 10;
            }

            // 4. Slice out ONLY the 20 items the UI actually asked for
            const startIndex = (page - 1) * pageSize;
            const endIndex = startIndex + pageSize;
            const pageItems = this.spotifyBuffer.uniqueTracks.slice(startIndex, endIndex);

            // 5. Fake the UI's PagedResponse exactly how it expects it!
            // If we have 60 items total, totalPages = 3. 
            // If hasMore is true, we pretend there is always at least 1 more page to keep the "Next" button alive.
            const fakeTotalPages = this.spotifyBuffer.hasMore
                ? Math.ceil((this.spotifyBuffer.uniqueTracks.length + pageSize) / pageSize)
                : Math.ceil(this.spotifyBuffer.uniqueTracks.length / pageSize);

            return {
                success: true,
                data: {
                    items: pageItems,
                    totalCount: this.spotifyBuffer.uniqueTracks.length,
                    totalPages: fakeTotalPages
                }
            };

        } catch (error) {
            console.error('Spotify Search error:', error);
            return { success: false, message: 'Network error while searching music.' };
        }
    }

    // Private helper to handle the actual search logic
    private async search(query: string, what: 'all' | 'movie' | 'series' | 'music', page: number = 1) {
        try {
            const response = await fetch(`${this.baseUrl}/search/${what}?query=${encodeURIComponent(query)}&page=${page}`);

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