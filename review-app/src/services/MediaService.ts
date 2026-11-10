import { apiFetch } from "./apiClient";
import { SpotifyParams, TmdbParams } from "../types/types";

/**
 * Wraps `api/Media`: title details, search and discovery for movies, series (TMDb) and music (Spotify).
 * The endpoints are anonymous but rate limited (`external-api`) to protect the TMDb / Spotify quotas.
 * Methods never throw; they resolve to `{ success, data }` or `{ success: false, message }`.
 */
class MediaService {
    private readonly baseUrl = 'https://localhost:7140/api/Media';

    /**
     * Accumulated Spotify results of the current music search, see `searchMusic`. Lives as long as the service
     * (until the page reloads), so going back to an earlier page of the same query costs no request.
     */
    private spotifyBuffer = {
        query: '',
        uniqueTracks: [] as any[],
        currentOffset: 0,
        hasMore: true
    };

    /**
     * Loads one title. The API fetches it from TMDb / Spotify on first use and caches it, so an unknown ID answers
     * 404 (`media_not_found`) and an outage of the source 503 (`source_unavailable`).
     * @param mediaType The `:mediaType` segment of the `/media/:mediaType/:externalApiId` route.
     * @param externalApiId The TMDb or Spotify ID.
     * @returns `{ success: true, data }` with the title, or `{ success: false, message }`.
     */
    async getMediaDetails(mediaType: string, externalApiId: string) {
        try {
            const response = await apiFetch(`${this.baseUrl}/media/${mediaType}/${externalApiId}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Failed to fetch media details' };
            }
        } catch (error) {
            console.error('Get media details error:', error);
            return { success: false, message: 'Network error while fetching media details.' };
        }
    }

    /**
     * Searches movies, series and music at once. One page only: the API returns a short mixed list (movies first,
     * then series, then music) without paging.
     * @param query The search text.
     * @returns `{ success: true, data }` with the titles, or `{ success: false, message }`.
     */
    async searchAll(query: string) {
        return this.search(query, 'all');
    }

    /**
     * Searches movies.
     * @param query The search text.
     * @param page 1-based page number.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async searchMovies(query: string, page: number = 1) {
        return this.search(query, 'movie', page);
    }

    /**
     * Searches series.
     * @param query The search text.
     * @param page 1-based page number.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async searchSeries(query: string, page: number = 1) {
        return this.search(query, 'series', page);
    }

    /**
     * Searches music with the same page shape as the TMDb searches. Spotify's API pages are smaller than the
     * 20 items the UI shows per page (10 per request), so the results are collected in `spotifyBuffer`: requests
     * are made until the buffer holds enough unique tracks (one already collected is skipped) for the wanted page,
     * which is then cut out of it. A new query resets the buffer.
     *
     * The reported `totalPages` is a guess while Spotify has more: one page beyond what is loaded, so the pager
     * keeps offering "next" until Spotify runs out.
     * @param query The search text.
     * @param page 1-based page number of 20 tracks.
     * @returns `{ success: true, data: { items, totalCount, totalPages } }`, where `totalCount` is the number of
     * tracks loaded so far, or `{ success: false, message }` when the network fails. A failing request mid-way
     * (HTTP error) ends the loop and returns what was gathered, so the user sees a short page instead of an error.
     */
    async searchMusic(query: string, page: number = 1) {
        try {
            if (this.spotifyBuffer.query !== query) {
                this.spotifyBuffer = {
                    query: query,
                    uniqueTracks: [],
                    currentOffset: 0,
                    hasMore: true
                };
            }

            const pageSize = 20;
            const targetTrackCount = page * pageSize;

            while (this.spotifyBuffer.uniqueTracks.length < targetTrackCount && this.spotifyBuffer.hasMore) {
                // The API pages by the Spotify page size (10 tracks), so the buffer's offset maps to its page number
                const cSharpPageToRequest = (this.spotifyBuffer.currentOffset / 10) + 1;

                const response = await apiFetch(`${this.baseUrl}/search/music?query=${encodeURIComponent(query)}&page=${cSharpPageToRequest}`);

                if (!response.ok) break;

                const data = await response.json();
                const fetchedItems = data.items;

                if (fetchedItems.length === 0) {
                    this.spotifyBuffer.hasMore = false;
                    break;
                }

                for (const item of fetchedItems) {
                    if (!this.spotifyBuffer.uniqueTracks.some(t => t.externalApiID === item.externalApiID)) {
                        this.spotifyBuffer.uniqueTracks.push(item);
                    }
                }

                this.spotifyBuffer.currentOffset += 10;
            }

            const startIndex = (page - 1) * pageSize;
            const endIndex = startIndex + pageSize;
            const pageItems = this.spotifyBuffer.uniqueTracks.slice(startIndex, endIndex);

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

    /**
     * Browses movies by filter (the TMDb discover endpoint).
     * @param params Page, sort order and the optional year, genre and runtime filters.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async discoverMovies(params: TmdbParams) {
        return this.discover(params, 'movie');
    }

    /**
     * Browses series by filter (the TMDb discover endpoint).
     * @param params Page, sort order and the optional year, genre and runtime filters.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async discoverSeries(params: TmdbParams) {
        return this.discover(params, 'series');
    }

    /**
     * Browses music by genre (Spotify). Optional filters left empty are not sent.
     * @param params Page, genre, optional year and the market.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async discoverMusic(params: SpotifyParams) {
        try {
            const query = new URLSearchParams();

            query.append('page', params.page.toString());
            query.append('genre', params.genre);
            if (params.year) query.append('year', params.year);
            query.append('market', params.market);

            const response = await apiFetch(`${this.baseUrl}/discover/music?${query.toString()}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Music Discovery failed' };
            }
        } catch (error) {
            console.error('Music Discover error:', error);
            return { success: false, message: 'Network error while discovering music.' };
        }
    }

    private async search(query: string, what: 'all' | 'movie' | 'series', page: number = 1) {
        try {
            const response = await apiFetch(`${this.baseUrl}/search/${what}?query=${encodeURIComponent(query)}&page=${page}`);

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

    private async discover(params: TmdbParams, what: 'movie' | 'series') {
        try {
            const query = new URLSearchParams();

            query.append('page', params.page.toString());
            query.append('sortBy', params.sortBy);

            if (params.year) query.append('year', params.year);
            if (params.withGenres) query.append('withGenres', params.withGenres);
            if (params.minRuntime) query.append('minRuntime', params.minRuntime);
            if (params.maxRuntime) query.append('maxRuntime', params.maxRuntime);

            const response = await apiFetch(`${this.baseUrl}/discover/${what}?${query.toString()}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Discovery failed' };
            }
        } catch (error) {
            console.error('Tmdb Discover error:', error);
            return { success: false, message: 'Network error while discovering.' };
        }
    }
}

export const mediaService = new MediaService();
