// User related DTOs
export interface UserProfileDto {
    id: number,
    username: string,
    bio?: string,
    profilePictureUrl?: string,
    createdAt: string,
    followersCount: number,
    followingCount: number,
    isFollowedByCurrentUser: boolean
}

// Media and Review related DTOs
export interface MediaDto {
    id: string;
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

// Collection related DTOs
export interface CollectionDto {
    id: number;
    name: string;
    visibilityLevel: number; // 0 = Private, 1 = Public, 2 = Followers Only
    createdAt: string;
    mediaCount: number;
    isOwner: boolean;
}

export interface CollectionMediaDto {
    dbMediaID: number,
    media: MediaDto;
    orderIndex: number;
    addedAt: string;
}

export interface CollectionWithMediasDto {
    collection: CollectionDto;
    mediaItems: CollectionMediaDto[];
}

// Follower System related DTOs
export interface UserFollowDto {
    id: number,
    username: string,
    profilePictureUrl?: string,
    isFollowedByCurrentUser: boolean
}

// Filter & sort related parameter dtos
export interface TmdbParams {
    page: number;
    sortBy: string;
    year?: string;
    withGenres?: string;
    minRuntime?: string;
    maxRuntime?: string;
}

export interface SpotifyParams {
    page: number;
    genre: string;
    year?: string;
    market: string;
}

export interface ReviewFilterParams {
    mediaType?: number; // 0 = Movie, 1 = Series, 2 = Music
    externalApiId?: string;
    page: number;
    sortBy: string;
    minScore: number;
    maxScore: number;
    hasWrittenText: boolean;
}
