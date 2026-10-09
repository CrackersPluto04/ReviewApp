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

export interface UserCompactDto {
    id: number,
    username: string,
    profilePictureUrl?: string,
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

// Review reply related DTOs
export interface ReplyDto {
    id: number;
    parentReplyID?: number | null;
    content?: string | null; // null for deleted replies
    username?: string | null; // null for deleted replies
    profilePictureUrl?: string | null;
    createdAt: string;
    isDeleted: boolean;
    isOwner: boolean;
    childCount: number; // non-deleted direct children
}

export interface CreateReplyDto {
    reviewID: number;
    parentReplyID?: number | null;
    content: string;
}

export interface ReplyPage {
    items: ReplyDto[];
    hasMore: boolean;
}

// Collection related DTOs
export interface CollectionDto {
    id: number;
    name: string;
    visibilityLevel: number; // 0 = Private, 1 = Public, 2 = Followers Only
    createdAt: string;
    mediaCount: number;
    isOwner: boolean;
    isDefault: boolean; // the permanent Favourites collection, can't be edited or deleted
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

// Achievement related DTOs
export type AchievementTierName = 'Bronze' | 'Silver' | 'Gold';

export interface AchievementTierDto {
    tier: AchievementTierName;
    description: string;
    targetValue: number;
    isUnlocked: boolean;
}

export interface AchievementDto {
    groupCode: string;
    title: string;
    iconKey: string; // icon file in public/achievements, without extension
    categories: string[]; // Movie, Series, Music, Review, Reply, Collection
    currentProgress: number; // shared by every tier
    tiers: AchievementTierDto[]; // bronze -> gold
}

export interface UnlockedAchievementDto {
    groupCode: string;
    title: string;
    tier: AchievementTierName;
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
