// User related DTOs
export interface UserProfileDto {
    id: number,
    username: string,
    bio?: string,
    profilePictureUrl?: string,
    createdAt: string,
    followersCount: number,
    followingCount: number,
    isFollowedByCurrentUser: boolean,
    isPremium: boolean // public Premium badge
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

// A single reply with its layer 2 parent (when it's a layer 3 reply), used to open a thread at it
export interface ReplyContextDto {
    reviewID: number;
    reply: ReplyDto;
    parentReply?: ReplyDto | null;
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

// Notification related DTOs
export type NotificationType = 'System' | 'ReviewReply' | 'ReplyReply' | 'AchievementUnlocked' | 'NewFollower'
    | 'SubscriptionStarted' | 'PaymentFailed' | 'SubscriptionCancelled' | 'SubscriptionEnded';
export type NotificationStatusFilter = 'all' | 'seen' | 'unseen';
export type NotificationSort = 'newest' | 'oldest';

export interface NotificationDto {
    id: number;
    type: NotificationType;
    title: string;
    message: string; // plain text, never rendered as HTML
    isSeen: boolean;
    createdAt: string; // ISO date, UTC
    actorUsername?: string | null; // null for system / achievement notifications
    actorProfilePictureUrl?: string | null;
    // Navigation target, which ones are set depends on the type
    reviewID?: number | null;
    replyID?: number | null;
    groupCode?: string | null;
}

export interface NotificationPage {
    items: NotificationDto[];
    hasMore: boolean;
    totalCount: number; // matching the current filter
    unseenCount: number;
}

export interface NotificationQueryParams {
    status: NotificationStatusFilter;
    sort: NotificationSort;
    afterId?: number;
    pageSize?: number;
}

// Account security
export interface SecurityStatusDto {
    email: string;
    isEmailConfirmed: boolean;
    isTwoFactorEnabled: boolean;
    pendingEmail?: string | null;
}

// Subscription
export type SubscriptionTier = 'Free' | 'Premium';
export type SubscriptionStatus = 'None' | 'Active' | 'PastDue' | 'Ended';

export interface SubscriptionStatusDto {
    tier: SubscriptionTier;
    status: SubscriptionStatus;
    isPremium: boolean;
    willRenew: boolean;
    cancelAtPeriodEnd: boolean; // cancelled, Premium runs until accessEndsAt
    hasPaymentProblem: boolean; // a renewal failed, Premium runs until accessEndsAt (grace period)
    currentPeriodEnd?: string | null; // ISO date, UTC
    accessEndsAt?: string | null;
}

export interface PlanFeatureDto {
    key: string;
    title: string;
    description: string;
    comingSoon: boolean;
}

export interface PlanDto {
    tier: SubscriptionTier;
    priceAmount: number;
    currency: string;
    interval: string;
    features: PlanFeatureDto[];
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
