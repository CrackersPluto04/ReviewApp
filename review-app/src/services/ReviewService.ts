import { ReviewMediaDto } from "../types/types";

class ReviewService {
    private readonly baseUrl = 'https://localhost:7140/api/Review';

    async getMediaReviews(externalApiId: string, mediaType: number) {
        const res = await fetch(`${this.baseUrl}/media?externalApiId=${externalApiId}&mediaType=${mediaType}`);
        return res.json();
    }

    async checkIfUserReviewedMedia(externalApiId: string, mediaType: number) {
        const res = await fetch(`${this.baseUrl}/check?externalApiId=${externalApiId}&mediaType=${mediaType}`, {
            headers: this.getHeaders()
        });

        return res.json();
    }

    async createReview(data: ReviewMediaDto) {
        const res = await fetch(this.baseUrl, {
            method: 'POST',
            headers: this.getHeaders(),
            body: JSON.stringify(data)
        });

        return res.json();
    }

    async editReview(data: ReviewMediaDto) {
        const res = await fetch(this.baseUrl, {
            method: 'PUT',
            headers: this.getHeaders(),
            body: JSON.stringify(data)
        });

        return res.json();
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