import { apiFetch } from "./apiClient";
import { ReviewFilterParams, ReviewMediaDto } from "../types/types";

/**
 * Wraps `api/Review`. Reading is open to anonymous visitors, with each review's visibility decided per viewer;
 * writing needs a login and touches the caller's own reviews only. There is at most one review per user and title.
 * Creating or editing may unlock achievements, which `apiFetch` picks up from the response header.
 * Methods never throw; they resolve to `{ success, data }` or `{ success: false, message }`.
 */
class ReviewService {
    private readonly baseUrl = 'https://localhost:7140/api/Review';

    /**
     * Lists the public reviews of one title, filtered, sorted and paged.
     * @param params The title (`mediaType`, `externalApiId`), the page and the sort and filter values; an empty
     * `sortBy` and `hasWrittenText: false` are not sent.
     * @returns `{ success: true, data }` with a paged response, or `{ success: false, message }`.
     */
    async getMediaReviews(params: ReviewFilterParams) {
        try {
            const query = new URLSearchParams();

            query.append('page', params.page.toString());
            if (params.sortBy) query.append('sortBy', params.sortBy);
            if (params.minScore !== undefined) query.append('minScore', params.minScore.toString());
            if (params.maxScore !== undefined) query.append('maxScore', params.maxScore.toString());
            if (params.hasWrittenText) query.append('hasWrittenText', 'true');

            const response = await apiFetch(`${this.baseUrl}/media/${params.mediaType}/${params.externalApiId}?${query.toString()}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting the reviews.' }
            }
        } catch (error) {
            console.error('GetMediaReviews error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }

    /**
     * Loads a single review with its author: the shareable review link and the target of reply notifications.
     * @param id The review's ID.
     * @returns `{ success: true, data }`, or a failure with `notFound` set when it doesn't exist or is hidden from
     * the viewer (the API answers the same for both, so the page shows one "not found" state).
     */
    async getReview(id: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${id}`, { credentials: 'include' });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json().catch(() => null);
                return { success: false, notFound: response.status === 404, message: errorData?.error || 'Something went wrong while getting the review.' }
            }
        } catch (error) {
            console.error('GetReview error:', error);
            return { success: false, notFound: false, message: 'Network error while reaching the review.' }
        }
    }

    /**
     * Loads the average score and review count of a title. Separate from the review list because every media card
     * asks for its own.
     * @param externalApiId The TMDb or Spotify ID.
     * @param mediaType The `MediaType` value (0 = movie, 1 = series, 2 = music).
     * @returns `{ success: true, data }` with `{ averageScore, reviewCount }`, or `{ success: false, message }`.
     */
    async getAverageScore(externalApiId: string, mediaType: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/stats/average-score?externalApiId=${externalApiId}&mediaType=${mediaType}`);

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while getting the average score.' }
            }
        } catch (error) {
            console.error('GetAverageScore error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }

    /**
     * Tells whether the logged-in user has reviewed a title and returns that review, so the form can start in
     * create or edit mode.
     * @param externalApiId The TMDb or Spotify ID.
     * @param mediaType The `MediaType` value.
     * @returns `{ success: true, data }` with `{ hasReviewed, reviewData }`, or `{ success: false, message }`.
     */
    async checkIfUserReviewedMedia(externalApiId: string, mediaType: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/check?externalApiId=${externalApiId}&mediaType=${mediaType}`, {
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include'
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while checking review status.' }
            }
        } catch (error) {
            console.error('Review status check error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }

    /**
     * Creates the logged-in user's review of a title. The API fetches the title from TMDb / Spotify the first time
     * anyone uses it, so an unknown title or an outage of the source fails here.
     * @param data The title and the review's score, text and visibility.
     * @returns `{ success: true, data }` with the API's confirmation, or a failure (e.g. the title is already reviewed).
     */
    async createReview(data: ReviewMediaDto) {
        try {
            const response = await apiFetch(this.baseUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify(data)
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while creating review. Please try again.' }
            }
        } catch (error) {
            console.error('Create review error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }

    /**
     * Edits the logged-in user's existing review of a title. The API finds it by user and title, so the request
     * has the same shape as a create.
     * @param data The title and the review's new values.
     * @returns `{ success: true, data }` with the API's confirmation, or a failure (e.g. there is no review to edit).
     */
    async editReview(data: ReviewMediaDto) {
        try {
            const response = await apiFetch(this.baseUrl, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify(data)
            });

            if (response.ok) {
                const data = await response.json();
                return { success: true, data: data };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while editing review. Please try again.' }
            }
        } catch (error) {
            console.error('Edit review error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }

    /**
     * Deletes one of the logged-in user's reviews together with all of its replies.
     * @param id The review's ID.
     * @returns `{ success: true, data: null }`, or `{ success: false, message }`.
     */
    async deleteReview(id: number) {
        try {
            const response = await apiFetch(`${this.baseUrl}/${id}`, {
                method: 'DELETE',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include'
            });

            if (response.ok) {
                return { success: true, data: null };
            } else {
                const errorData = await response.json();
                return { success: false, message: errorData.error || 'Something went wrong while deleting review. Please try again.' }
            }
        } catch (error) {
            console.error('Delete review error:', error);
            return { success: false, message: 'Network error while reaching reviews.' }
        }
    }
}

export const reviewService = new ReviewService();
