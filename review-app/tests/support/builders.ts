import type { AuthUser } from '../../src/context/AuthContext';
import type {
    AchievementDto, AchievementTierDto, CollectionDto, CollectionMediaDto, MediaDto, NotificationDto, PlanDto,
    ReplyDto, SecurityStatusDto, SubscriptionStatusDto, UnlockedAchievementDto, UserCompactDto, UserProfileDto,
} from '../../src/types/types';

/**
 * Builders for the API's DTOs: valid defaults, overridden per test with only what the test is about.
 * Ids come from a counter, so two built objects never collide.
 */
let nextId = 1;
export const id = () => nextId++;

export const MOVIE = 0;
export const SERIES = 1;
export const MUSIC = 2;

export const PUBLIC = 1;
export const PRIVATE = 0;
export const FOLLOWERS_ONLY = 2;

export function authUser(overrides: Partial<AuthUser> = {}): AuthUser {
    return {
        id: id(),
        username: 'alice',
        profilePictureUrl: undefined,
        isEmailConfirmed: true,
        tier: 'Free',
        isPremium: false,
        entitlements: [],
        ...overrides,
    };
}

export function premiumAuthUser(overrides: Partial<AuthUser> = {}): AuthUser {
    return authUser({ tier: 'Premium', isPremium: true, entitlements: ['entitlement-preview'], ...overrides });
}

export function userProfile(overrides: Partial<UserProfileDto> = {}): UserProfileDto {
    return {
        id: id(),
        username: 'bob',
        bio: undefined,
        profilePictureUrl: undefined,
        createdAt: '2026-01-15T10:00:00Z',
        followersCount: 0,
        followingCount: 0,
        isFollowedByCurrentUser: false,
        isPremium: false,
        ...overrides,
    };
}

export function compactUser(overrides: Partial<UserCompactDto> = {}): UserCompactDto {
    return {
        id: id(),
        username: 'carol',
        profilePictureUrl: undefined,
        isFollowedByCurrentUser: false,
        ...overrides,
    };
}

export function media(overrides: Partial<MediaDto> = {}): MediaDto {
    const n = id();
    return {
        id: String(n),
        externalApiID: `ext-${n}`,
        mediaType: MOVIE,
        title: `Movie ${n}`,
        releaseDate: '2020-05-01',
        posterUrl: `https://img.example/poster-${n}.jpg`,
        overview: 'An overview.',
        creator: undefined,
        ...overrides,
    };
}

export const movie = (overrides: Partial<MediaDto> = {}) => media({ mediaType: MOVIE, ...overrides });
export const series = (overrides: Partial<MediaDto> = {}) => media({ mediaType: SERIES, title: `Series ${nextId}`, ...overrides });
export const album = (overrides: Partial<MediaDto> = {}) => media({ mediaType: MUSIC, title: `Album ${nextId}`, creator: 'Some Artist', ...overrides });

// A review as the list endpoints return it (author and media included)
export function review(overrides: Record<string, any> = {}) {
    const n = id();
    return {
        id: n,
        score: 8,
        reviewText: 'Really good.',
        pros: undefined,
        cons: undefined,
        visibilityLevel: PUBLIC,
        createdAt: '2026-03-10T12:00:00Z',
        updatedAt: '2026-03-10T12:00:00Z',
        username: 'bob',
        profilePictureUrl: undefined,
        replyCount: 0,
        media: movie(),
        ...overrides,
    };
}

export function reply(overrides: Partial<ReplyDto> = {}): ReplyDto {
    return {
        id: id(),
        parentReplyID: null,
        content: 'Nice review!',
        username: 'carol',
        profilePictureUrl: null,
        createdAt: '2026-03-11T08:30:00Z',
        isDeleted: false,
        isOwner: false,
        childCount: 0,
        ...overrides,
    };
}

export function deletedReply(overrides: Partial<ReplyDto> = {}): ReplyDto {
    return reply({ content: null, username: null, isDeleted: true, ...overrides });
}

export function collection(overrides: Partial<CollectionDto> = {}): CollectionDto {
    const n = id();
    return {
        id: n,
        name: `Collection ${n}`,
        visibilityLevel: PUBLIC,
        createdAt: '2026-02-01T09:00:00Z',
        mediaCount: 0,
        isOwner: false,
        isDefault: false,
        ...overrides,
    };
}

export function favourites(overrides: Partial<CollectionDto> = {}): CollectionDto {
    return collection({ name: 'Favourites', isDefault: true, ...overrides });
}

export function collectionMedia(overrides: Partial<CollectionMediaDto> = {}): CollectionMediaDto {
    const item = overrides.media ?? movie();
    return {
        dbMediaID: Number(item.id),
        media: item,
        orderIndex: 0,
        addedAt: '2026-02-02T09:00:00Z',
        ...overrides,
    };
}

export function tier(overrides: Partial<AchievementTierDto> = {}): AchievementTierDto {
    return {
        tier: 'Bronze',
        description: 'Do it once',
        targetValue: 1,
        isUnlocked: false,
        ...overrides,
    };
}

// Bronze / silver / gold at 1 / 10 / 50, unlocked up to the progress
export function achievement(overrides: Partial<AchievementDto> = {}): AchievementDto {
    const progress = overrides.currentProgress ?? 0;
    const targets: [AchievementTierDto['tier'], number][] = [['Bronze', 1], ['Silver', 10], ['Gold', 50]];
    return {
        groupCode: `GROUP_${id()}`,
        title: 'Critic',
        iconKey: 'critic',
        categories: ['Review'],
        currentProgress: progress,
        tiers: targets.map(([name, target]) => tier({
            tier: name,
            targetValue: target,
            description: `Write ${target} reviews`,
            isUnlocked: progress >= target,
        })),
        ...overrides,
    };
}

export function unlocked(overrides: Partial<UnlockedAchievementDto> = {}): UnlockedAchievementDto {
    return { groupCode: 'CRITIC', title: 'Critic', tier: 'Bronze', ...overrides };
}

export function notification(overrides: Partial<NotificationDto> = {}): NotificationDto {
    return {
        id: id(),
        type: 'System',
        title: 'Welcome',
        message: 'Thanks for joining.',
        isSeen: false,
        createdAt: '2026-04-01T14:05:00Z',
        actorUsername: null,
        actorProfilePictureUrl: null,
        reviewID: null,
        replyID: null,
        groupCode: null,
        ...overrides,
    };
}

export function notificationPage(items: NotificationDto[], overrides: Record<string, any> = {}) {
    return {
        items,
        hasMore: false,
        totalCount: items.length,
        unseenCount: items.filter(n => !n.isSeen).length,
        ...overrides,
    };
}

export function securityStatus(overrides: Partial<SecurityStatusDto> = {}): SecurityStatusDto {
    return {
        email: 'alice@example.com',
        isEmailConfirmed: true,
        isTwoFactorEnabled: false,
        pendingEmail: null,
        ...overrides,
    };
}

export function plan(overrides: Partial<PlanDto> = {}): PlanDto {
    return {
        tier: 'Premium',
        priceAmount: 4.99,
        currency: 'EUR',
        interval: 'month',
        features: [
            { key: 'entitlement-preview', title: 'Early access', description: 'Try new features first.', comingSoon: false },
            { key: 'stats', title: 'Statistics', description: 'Charts about your reviews.', comingSoon: true },
        ],
        ...overrides,
    };
}

export function subscriptionStatus(overrides: Partial<SubscriptionStatusDto> = {}): SubscriptionStatusDto {
    return {
        tier: 'Free',
        status: 'None',
        isPremium: false,
        willRenew: false,
        cancelAtPeriodEnd: false,
        hasPaymentProblem: false,
        currentPeriodEnd: null,
        accessEndsAt: null,
        ...overrides,
    };
}

export function activeSubscription(overrides: Partial<SubscriptionStatusDto> = {}): SubscriptionStatusDto {
    return subscriptionStatus({
        tier: 'Premium',
        status: 'Active',
        isPremium: true,
        willRenew: true,
        currentPeriodEnd: '2026-12-01T00:00:00Z',
        accessEndsAt: '2026-12-01T00:00:00Z',
        ...overrides,
    });
}
