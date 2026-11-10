/**
 * @module types
 * The shapes of the API's JSON, grouped by feature. They mirror the backend DTOs (`ReviewApp.Api/DTOs`) by hand,
 * with the API's casing (note `externalApiID` in JSON bodies but `externalApiId` in query parameters), so a change to a
 * DTO must be repeated here. Dates are ISO 8601 strings, UTC.
 */

// User related DTOs

/** A user's public profile. */
export interface UserProfileDto {
    id: number,
    username: string,
    bio?: string,
    profilePictureUrl?: string,
    createdAt: string,
    followersCount: number,
    followingCount: number,
    /** The logged-in viewer's follow state; `false` for anonymous viewers. */
    isFollowedByCurrentUser: boolean,
    /** Shows the public Premium badge. */
    isPremium: boolean
}

/** A user as listed in search results and follower / following lists. */
export interface UserCompactDto {
    id: number,
    username: string,
    profilePictureUrl?: string,
    isFollowedByCurrentUser: boolean
}

// Media and Review related DTOs

/** A movie, series or track. Also the media part of review and collection requests. */
export interface MediaDto {
    /** A key unique across media types (the external ID with the media type appended). Not the database ID. */
    id: string;
    /** The TMDb or Spotify ID, unique together with `mediaType`. */
    externalApiID: string;
    /** 0 = Movie, 1 = Series, 2 = Music. */
    mediaType: number;
    title: string;
    /** The source's release date text, not normalized (Spotify may give only a year). */
    releaseDate?: string;
    posterUrl?: string;
    overview?: string;
    /** The artists for music; for movies and series only filled from the title's details, empty in search results. */
    creator?: string;
}

/** The user-written part of a review. */
export interface ReviewDto {
    /** 1 to 10. */
    score: number;
    reviewText?: string;
    pros?: string;
    cons?: string;
    /** 0 = Private, 1 = Public, 2 = Followers Only. */
    visibilityLevel: number;
}

/** Request body to create or edit a review: the title and the review. */
export interface ReviewMediaDto {
    mediaDto: MediaDto;
    reviewDto: ReviewDto;
}

// Review reply related DTOs

/** A reply as shown in a thread. */
export interface ReplyDto {
    id: number;
    parentReplyID?: number | null;
    /** `null` for deleted replies: the text stays in the database but is never returned. */
    content?: string | null;
    /** `null` for deleted replies. */
    username?: string | null;
    profilePictureUrl?: string | null;
    createdAt: string;
    /** A deleted reply is shown as a placeholder while it still has answers. */
    isDeleted: boolean;
    /** Whether the viewer wrote it, so the UI can offer delete. */
    isOwner: boolean;
    /** The number of non-deleted direct children. */
    childCount: number;
}

/** Request body to post a reply. */
export interface CreateReplyDto {
    reviewID: number;
    /** Omitted when replying directly to the review; otherwise a layer-2 reply. */
    parentReplyID?: number | null;
    content: string;
}

/** One keyset ("load more") page of replies. */
export interface ReplyPage {
    items: ReplyDto[];
    hasMore: boolean;
}

/** A single reply with its layer 2 parent (when it's a layer 3 reply), used to open a thread at it. */
export interface ReplyContextDto {
    reviewID: number;
    reply: ReplyDto;
    parentReply?: ReplyDto | null;
}

// Collection related DTOs

/** A collection as shown in lists and headers. */
export interface CollectionDto {
    id: number;
    name: string;
    /** 0 = Private, 1 = Public, 2 = Followers Only. */
    visibilityLevel: number;
    createdAt: string;
    mediaCount: number;
    /** Whether the viewer owns it, so the UI can offer editing. */
    isOwner: boolean;
    /** The permanent Favourites collection, which can't be renamed, edited or deleted. */
    isDefault: boolean;
}

/** One title in a collection. */
export interface CollectionMediaDto {
    /** The stored (database) media ID, used to reorder; not the external API ID. */
    dbMediaID: number,
    media: MediaDto;
    orderIndex: number;
    addedAt: string;
}

/** A collection with its titles in the saved order. */
export interface CollectionWithMediasDto {
    collection: CollectionDto;
    mediaItems: CollectionMediaDto[];
}

// Achievement related DTOs

export type AchievementTierName = 'Bronze' | 'Silver' | 'Gold';

/** One tier of an achievement. */
export interface AchievementTierDto {
    tier: AchievementTierName;
    description: string;
    /** The count `currentProgress` has to reach to unlock this tier. */
    targetValue: number;
    isUnlocked: boolean;
}

/** An achievement card: the tiers of one group share its progress. */
export interface AchievementDto {
    groupCode: string;
    title: string;
    /** The icon file in `public/achievements`, without the extension. */
    iconKey: string;
    /** Any of Movie, Series, Music, Review, Reply, Collection. */
    categories: string[];
    /** Shared by every tier. */
    currentProgress: number;
    /** In order, bronze to gold. */
    tiers: AchievementTierDto[];
}

/** A tier unlocked by an action, as reported in the `X-Unlocked-Achievements` response header. */
export interface UnlockedAchievementDto {
    groupCode: string;
    title: string;
    tier: AchievementTierName;
}

// Notification related DTOs

/** The last four types open the Premium page. */
export type NotificationType = 'System' | 'ReviewReply' | 'ReplyReply' | 'AchievementUnlocked' | 'NewFollower'
    | 'SubscriptionStarted' | 'PaymentFailed' | 'SubscriptionCancelled' | 'SubscriptionEnded';
export type NotificationStatusFilter = 'all' | 'seen' | 'unseen';
export type NotificationSort = 'newest' | 'oldest';

/** A notification with ready-to-display texts. */
export interface NotificationDto {
    id: number;
    type: NotificationType;
    title: string;
    /** Plain text, never rendered as HTML. */
    message: string;
    isSeen: boolean;
    /** ISO date, UTC. */
    createdAt: string;
    /** `null` for system and achievement notifications. */
    actorUsername?: string | null;
    actorProfilePictureUrl?: string | null;
    /**
     * The navigation target. Plain IDs without a link to the content, so it can outlive what it points at;
     * which of them are set depends on the type (see `getNotificationRoute`).
     */
    reviewID?: number | null;
    replyID?: number | null;
    groupCode?: string | null;
}

/** One keyset page of notifications. */
export interface NotificationPage {
    items: NotificationDto[];
    hasMore: boolean;
    /** Matching the current filter. */
    totalCount: number;
    unseenCount: number;
}

/** Query of the notification list. */
export interface NotificationQueryParams {
    status: NotificationStatusFilter;
    sort: NotificationSort;
    /** Cursor: the ID of the last notification of the previous page. */
    afterId?: number;
    pageSize?: number;
}

// Account security

/** The account's security state, the only source of the email and 2FA state. */
export interface SecurityStatusDto {
    email: string;
    isEmailConfirmed: boolean;
    isTwoFactorEnabled: boolean;
    /** The new address of an email change that is not confirmed yet. */
    pendingEmail?: string | null;
}

// Subscription

/** What the user can use right now. */
export type SubscriptionTier = 'Free' | 'Premium';
/** The stored subscription state, which can differ from the tier (e.g. grace periods). */
export type SubscriptionStatus = 'None' | 'Active' | 'PastDue' | 'Ended';

/** The logged-in user's subscription. */
export interface SubscriptionStatusDto {
    tier: SubscriptionTier;
    status: SubscriptionStatus;
    isPremium: boolean;
    willRenew: boolean;
    /** Cancelled, but Premium runs until `accessEndsAt`. */
    cancelAtPeriodEnd: boolean;
    /** A renewal failed, but Premium runs until `accessEndsAt` (the grace period). */
    hasPaymentProblem: boolean;
    /** The end of the paid billing period. */
    currentPeriodEnd?: string | null;
    /** When Premium access stops; `null` when not entitled. */
    accessEndsAt?: string | null;
}

// Camera recognition (Premium)

/** How closely a result relates to the photo, closest first. */
export type RecognitionTier = 'direct' | 'universe' | 'people' | 'similar';
export type RecognitionOutcome = 'recognised' | 'nothing_recognised' | 'not_media_related' | 'person_not_identified';

/** One suggestion: a normal media item and its tier. */
export interface RecognizedMediaDto {
    media: MediaDto;
    tier: RecognitionTier;
}

/** Availability of camera recognition and what is left of the daily allowance. */
export interface RecognitionAllowanceDto {
    /** `false`: the feature is switched off. */
    enabled: boolean;
    /** Whether the user may use it; the counters are `null` when `false`. */
    premium: boolean;
    limit?: number | null;
    remaining?: number | null;
    /** When the allowance resets, UTC. */
    resetsAt?: string | null;
}

/** A slice of a stored recognition result. */
export interface RecognitionItemsDto {
    items: RecognizedMediaDto[];
    /** The size of the whole stored list, not just this page. */
    total: number;
    hasMore: boolean;
    /** The `offset` to ask for the next page. */
    nextOffset: number;
}

/** The answer to a recognition: the outcome, what was seen and the first page of the ranked list. */
export interface RecognitionResponseDto extends RecognitionItemsDto {
    /** `null` when there is nothing to page through; "Show more" needs it. */
    recognitionId?: string | null;
    outcome: RecognitionOutcome;
    /** A plain-text sentence beginning "We saw ...". */
    summary: string;
    /** What the AI saw, e.g. kind `statue` or `poster`. */
    subject?: { label: string, kind: string } | null;
    /** Some lookups failed, so the list may miss items. */
    incomplete: boolean;
    allowance: RecognitionAllowanceDto;
}

/** One feature on the plans page. */
export interface PlanFeatureDto {
    /** The stable feature key, the one `[RequiresFeature]` endpoints use. */
    key: string;
    title: string;
    description: string;
    /** Listed but not granted yet. */
    comingSoon: boolean;
}

/** The Premium plan. The price shown must match the payment provider's. */
export interface PlanDto {
    tier: SubscriptionTier;
    priceAmount: number;
    currency: string;
    interval: string;
    features: PlanFeatureDto[];
}

// Filter & sort related parameter dtos

/** Filters of the TMDb discover pages (movies and series). Values go to TMDb as they are. */
export interface TmdbParams {
    /** 1-based. */
    page: number;
    /** TMDb's `sort_by` value, e.g. `popularity.desc`. */
    sortBy: string;
    year?: string;
    /** TMDb genre IDs as TMDb's `with_genres` expects them. */
    withGenres?: string;
    /** Runtime bounds in minutes. */
    minRuntime?: string;
    maxRuntime?: string;
}

/** Filters of the music discover page. */
export interface SpotifyParams {
    /** 1-based; Spotify pages hold 10 tracks. */
    page: number;
    genre: string;
    year?: string;
    /** Two-letter country code that decides which tracks are available. */
    market: string;
}

/** Page, sort and filter values of the review lists (per title and per user). */
export interface ReviewFilterParams {
    /** 0 = Movie, 1 = Series, 2 = Music; only for the per-title list. */
    mediaType?: number;
    /** The TMDb or Spotify ID; only for the per-title list. */
    externalApiId?: string;
    /** 1-based. */
    page: number;
    /** E.g. `created_desc`, `score_asc`; the API's list of sort keys applies. */
    sortBy: string;
    /** The score range, 1 to 10. */
    minScore: number;
    maxScore: number;
    /** Only reviews that have written text, not just a score. */
    hasWrittenText: boolean;
}
