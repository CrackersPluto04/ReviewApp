import { ReviewMediaDto } from "../types/types";

class ReviewService {
    private readonly baseUrl = 'https://localhost:7140/api/Review';

    async getMediaReviews(externalApiId: string, mediaType: number) {
        try {
            const response = await fetch(`${this.baseUrl}/media?externalApiId=${externalApiId}&mediaType=${mediaType}`);

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

    async getAverageScore(externalApiId: string, mediaType: number) {
        try {
            const response = await fetch(`${this.baseUrl}/stats/average-score?externalApiId=${externalApiId}&mediaType=${mediaType}`);

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

    async checkIfUserReviewedMedia(externalApiId: string, mediaType: number) {
        try {
            const response = await fetch(`${this.baseUrl}/check?externalApiId=${externalApiId}&mediaType=${mediaType}`, {
                headers: this.getHeaders()
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

    async createReview(data: ReviewMediaDto) {
        try {
            const response = await fetch(this.baseUrl, {
                method: 'POST',
                headers: this.getHeaders(),
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

    async editReview(data: ReviewMediaDto) {
        try {
            const response = await fetch(this.baseUrl, {
                method: 'PUT',
                headers: this.getHeaders(),
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

    // Private helper to grab the token from localStorage
    private getHeaders() {
        const token = localStorage.getItem('jwt_token');
        return {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        };
    }
}

export const reviewService = new ReviewService();