export interface MediaDto {
    externalApiID: string;
    mediaType: number; // 0 = Movie, 1 = Series, 2 = Music
    title: string;
    releaseDate?: string;
    posterUrl?: string;
    overview?: string;
    creator?: string;
}

export interface ReviewDto {
    score: number;
    reviewText?: string;
    pros?: string;
    cons?: string;
    visibilityLevel: number; // 0 = Private, 1 = Public, 2 = Followers Only
}

export interface ReviewMediaDto {
    mediaDto: MediaDto;
    reviewDto: ReviewDto;
}