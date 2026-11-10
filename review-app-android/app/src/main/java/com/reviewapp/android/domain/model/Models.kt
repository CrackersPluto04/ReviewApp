package com.reviewapp.android.domain.model

// The app's own view of the API's data: plain Kotlin types, mapped from the DTOs with `toDomain()`.
// Enums that come from the API read an unknown value as a safe default, so a newer backend never crashes an older app.

/** Same values as the API's `MediaType` enum. [pathSegment] is the name used in search/discover routes. */
enum class MediaType(val id: Int, val label: String, val pathSegment: String) {
    Movie(0, "Movie", "movie"),
    Series(1, "Series", "series"),
    Music(2, "Music", "music");

    companion object {
        fun fromId(id: Int): MediaType = entries.firstOrNull { it.id == id } ?: Movie
    }
}

/** Same values as the API's `VisibilityLevel` enum (stored there as integers). An unknown id reads as [Public]. */
enum class Visibility(val id: Int, val label: String) {
    Public(1, "Public"),
    FollowersOnly(2, "Followers only"),
    Private(0, "Private");

    companion object {
        fun fromId(id: Int): Visibility = entries.firstOrNull { it.id == id } ?: Public
    }
}

/** A movie, series or track. Unique by [mediaType] and [externalApiId] (the TMDb or Spotify ID), see [key]. */
data class Media(
    val externalApiId: String,
    val mediaType: MediaType,
    val title: String,
    val releaseDate: String?,
    val posterUrl: String?,
    val overview: String,
    val creator: String,
) {
    /** The identity of this title, used as a list key and in the caches. */
    val key: String get() = mediaKey(mediaType, externalApiId)
}

/** The identity of a title: the media type and its external API ID, which are only unique together. */
fun mediaKey(mediaType: MediaType, externalApiId: String) = "${mediaType.id}:$externalApiId"

/** The logged-in user, as returned by login and check-auth. */
data class AuthUser(
    val id: Int,
    val username: String,
    val profilePictureUrl: String?,
    val isEmailConfirmed: Boolean,
    /** Subscription state at the last check-auth; the API checks access on every request anyway (402 when missing). */
    val isPremium: Boolean = false,
    val entitlements: Set<String> = emptySet(),
) {
    /** For showing or hiding UI only, see [FeatureKeys]. */
    fun hasFeature(key: String) = key in entitlements
}

/** The community average score of a title and how many reviews it is based on. */
data class ScoreStats(val averageScore: Double, val reviewCount: Int)

/** A review in the list on a media's page. */
data class MediaReview(
    val id: Int,
    val score: Double,
    val reviewText: String?,
    val pros: String?,
    val cons: String?,
    val username: String,
    val profilePictureUrl: String?,
    val createdAt: String,
    val updatedAt: String,
    val replyCount: Int,
)

/** A review in the home feed, it also carries the reviewed media. */
data class FeedReview(
    val id: Int,
    val username: String,
    val profilePictureUrl: String?,
    val mediaTitle: String,
    val posterUrl: String?,
    val mediaType: MediaType,
    val externalApiId: String,
    val score: Double,
    val reviewText: String?,
    val pros: String?,
    val cons: String?,
    val createdAt: String,
    val replyCount: Int,
)

/** The editable part of the current user's own review. */
data class ReviewDraft(
    val score: Double,
    val reviewText: String,
    val pros: String,
    val cons: String,
    val visibility: Visibility,
)

/** A numbered page of a list; [page] is 1-based. */
data class Page<T>(
    val items: List<T>,
    val page: Int,
    val totalPages: Int,
    val totalCount: Int,
) {
    val hasMore: Boolean get() = page < totalPages
}

data class UserProfile(
    val id: Int,
    val username: String,
    val bio: String,
    val profilePictureUrl: String?,
    val joinedAt: String,
    val followersCount: Int,
    val followingCount: Int,
    /** Whether the current user follows them, always false for a logged-out viewer and the owner. */
    val isFollowed: Boolean,
    /** The public Premium badge. */
    val isPremium: Boolean = false,
)

/** A user in search results and follower lists. */
data class UserSummary(
    val id: Int,
    val username: String,
    val profilePictureUrl: String?,
    val isFollowed: Boolean,
)

/** A review in a user's profile, with the reviewed media instead of the author. */
data class ProfileReview(
    val id: Int,
    val mediaTitle: String,
    val posterUrl: String?,
    val mediaType: MediaType,
    val externalApiId: String,
    val score: Double,
    val reviewText: String?,
    val pros: String?,
    val cons: String?,
    val createdAt: String,
    val updatedAt: String,
    val visibility: Visibility,
    val isOwner: Boolean,
    val replyCount: Int,
    /** Offline mode: the change waiting to sync ([com.reviewapp.android.data.local.offline.SyncState]). */
    val syncState: Int = 0,
) {
    val mediaKey: String get() = mediaKey(mediaType, externalApiId)
}

/** A collection as listed on a profile; [syncState] is set when it has a change waiting to sync (offline mode). */
data class MediaCollection(
    val id: Int,
    val name: String,
    val visibility: Visibility,
    val createdAt: String,
    val mediaCount: Int,
    val isOwner: Boolean,
    /** The permanent "Favourites" collection, it can't be renamed, edited or deleted. */
    val isDefault: Boolean,
    val syncState: Int = 0,
)

/** A media in a collection; [dbMediaId] is the API's own id, needed for reordering. */
data class CollectionItem(val dbMediaId: Int, val media: Media, val syncState: Int = 0)

/** A collection with its titles in the saved order. */
data class CollectionDetails(val collection: MediaCollection, val items: List<CollectionItem>)

/** A single review with its author (the review page). */
data class ReviewDetail(
    val review: ProfileReview,
    val username: String,
    val profilePictureUrl: String?,
)

/** A reply in a thread. Threads are at most three layers: review, reply, reply to a reply. */
data class Reply(
    val id: Int,
    /** Null for a reply to the review (layer 2), set for a reply to a reply (layer 3). */
    val parentReplyId: Int?,
    val content: String,
    /** Null for a deleted reply. */
    val username: String?,
    val profilePictureUrl: String?,
    val createdAt: String,
    val isDeleted: Boolean,
    val isOwner: Boolean,
    /** Non-deleted direct replies to this one. */
    val childCount: Int,
) {
    val isTopLevel: Boolean get() = parentReplyId == null
}

/** A keyset ("load more") page of replies. */
data class ReplyPage(val items: List<Reply>, val hasMore: Boolean)

/** A reply with its layer-2 parent (when it is a layer-3 reply), used to open a thread at it. */
data class ReplyContext(val reviewId: Int, val reply: Reply, val parentReply: Reply?)

/** The kinds of notification; the last four open the Premium screen. [label] is the short chip text. */
enum class NotificationType(val label: String) {
    System("System"),
    ReviewReply("Reply"),
    ReplyReply("Reply"),
    AchievementUnlocked("Achievement"),
    NewFollower("Follower"),
    SubscriptionStarted("Premium"),
    PaymentFailed("Premium"),
    SubscriptionCancelled("Premium"),
    SubscriptionEnded("Premium");

    companion object {
        /** The API sends the enum's name; anything unknown is shown as a system message. */
        fun fromName(name: String?): NotificationType = entries.firstOrNull { it.name == name } ?: System
    }
}

/** A notification with ready-to-show texts and the ids of what it points to (which may be deleted since). */
data class AppNotification(
    val id: Int,
    val type: NotificationType,
    val title: String,
    val message: String,
    val isSeen: Boolean,
    val createdAt: String,
    val actorUsername: String?,
    val actorProfilePictureUrl: String?,
    val reviewId: Int?,
    val replyId: Int?,
    val groupCode: String?,
    val syncState: Int = 0,
)

/** Keyset page; [unseenCount] is the user's overall count, for the badge. */
data class NotificationPage(
    val items: List<AppNotification>,
    val hasMore: Boolean,
    val totalCount: Int,
    val unseenCount: Int,
)

/** The list's `status` filter, [apiValue] is the query parameter. */
enum class NotificationStatus(val apiValue: String) {
    All("all"), Seen("seen"), Unseen("unseen");

    companion object {
        /** The Seen / Unseen toggles: exactly one on filters by it, both or neither shows everything. */
        fun from(seenOn: Boolean, unseenOn: Boolean): NotificationStatus = when {
            seenOn && !unseenOn -> Seen
            unseenOn && !seenOn -> Unseen
            else -> All
        }
    }
}

/** The order of the notification list, [apiValue] is the query parameter. */
enum class NotificationSort(val apiValue: String, val label: String) {
    Newest("newest", "Newest first"), Oldest("oldest", "Oldest first")
}

/** The tiers of an achievement, in order. An unknown name reads as [Bronze]. */
enum class AchievementTier {
    Bronze, Silver, Gold;

    companion object {
        fun fromName(name: String?): AchievementTier = entries.firstOrNull { it.name == name } ?: Bronze
    }
}

/** The categories an achievement can be in (the names of the API's category flags). */
enum class AchievementCategory {
    Movie, Series, Music, Review, Reply, Collection;

    companion object {
        fun fromName(name: String): AchievementCategory? = entries.firstOrNull { it.name == name }
    }
}

/** One tier of an achievement: what it asks for and whether it is earned. */
data class AchievementTierInfo(
    val tier: AchievementTier,
    val description: String,
    val targetValue: Int,
    val isUnlocked: Boolean,
)

/** One achievement card: its tiers share [currentProgress] (the highest count ever reached). */
data class Achievement(
    val groupCode: String,
    val title: String,
    val iconKey: String,
    val categories: Set<AchievementCategory>,
    val currentProgress: Int,
    /** Bronze -> Silver -> Gold. */
    val tiers: List<AchievementTierInfo>,
) {
    /** How many tiers are earned. */
    val unlockedCount: Int get() = tiers.count { it.isUnlocked }
    /** The first tier not unlocked yet, null when completed. */
    val nextTier: AchievementTierInfo? get() = tiers.firstOrNull { !it.isUnlocked }
    /** Whether every tier is earned (an achievement without tiers is never completed). */
    val isCompleted: Boolean get() = tiers.isNotEmpty() && tiers.all { it.isUnlocked }
}

/** A tier unlocked by the current user's last request (the `X-Unlocked-Achievements` header). */
data class UnlockedAchievement(val groupCode: String, val title: String, val tier: AchievementTier)

/** `GET Security/status`: the only place with the email, the pending change and the 2FA state. */
data class SecurityStatus(
    val email: String,
    val isEmailConfirmed: Boolean,
    val isTwoFactorEnabled: Boolean,
    /** A new address waiting for its confirmation link to be opened. */
    val pendingEmail: String?,
)

/** Keys of the features that need a subscription, same as the API's `FeatureKeys`. */
object FeatureKeys {
    const val PROFILE_BADGE = "profile-badge"
    const val CAMERA_RECOGNITION = "camera-recognition"
    const val OFFLINE_MODE = "offline-mode"
}

/** How closely a recognition suggestion relates to the photo, closest first (the API's tier names). */
enum class RecognitionTier(val apiName: String, val label: String) {
    Direct("direct", "Direct match"),
    Universe("universe", "Same universe"),
    People("people", "Related people"),
    Similar("similar", "You might also like");

    companion object {
        /** A tier the app doesn't know yet reads as the loosest one. */
        fun fromApi(name: String?): RecognitionTier = entries.firstOrNull { it.apiName == name } ?: Similar
    }
}

/** How a recognition ended; an unknown value reads as [NothingRecognised]. */
enum class RecognitionOutcome(val apiName: String) {
    Recognised("recognised"),
    NothingRecognised("nothing_recognised"),
    NotMediaRelated("not_media_related"),
    PersonNotIdentified("person_not_identified");

    companion object {
        fun fromApi(name: String?): RecognitionOutcome = entries.firstOrNull { it.apiName == name } ?: NothingRecognised
    }
}

/** One suggestion of a recognition: a title and how closely it relates to the photo. */
data class RecognizedMedia(val media: Media, val tier: RecognitionTier)

/** `GET Recognition/allowance`: whether the feature is on, and the daily allowance (null counters for Free users). */
data class RecognitionAllowance(
    val enabled: Boolean,
    val premium: Boolean,
    val limit: Int?,
    val remaining: Int?,
    /** ISO date, UTC. */
    val resetsAt: String?,
)

/** A page of a recognition's ranked list. */
data class RecognitionItems(
    val items: List<RecognizedMedia>,
    val total: Int,
    val hasMore: Boolean,
    val nextOffset: Int,
)

/** `POST Recognition`: what was seen and the first page of suggestions. */
data class Recognition(
    /** Null when there is nothing to page through. */
    val recognitionId: String?,
    val outcome: RecognitionOutcome,
    val summary: String,
    val firstPage: RecognitionItems,
    /** Some TMDb / Spotify lookups failed, the list may miss items. */
    val incomplete: Boolean,
    val allowance: RecognitionAllowance,
)

/** Same names as the API's `SubscriptionStatus`. */
enum class SubscriptionStatus {
    None, Active, PastDue, Ended;

    companion object {
        fun fromName(name: String?): SubscriptionStatus = entries.firstOrNull { it.name == name } ?: None
    }
}

/** `GET Subscription/status`: the current user's subscription, never provider details. */
data class SubscriptionSummary(
    val status: SubscriptionStatus,
    val isPremium: Boolean,
    val willRenew: Boolean,
    /** Cancelled, Premium runs until [accessEndsAt]. */
    val cancelAtPeriodEnd: Boolean,
    /** A renewal failed, Premium runs until [accessEndsAt] (the grace period). */
    val hasPaymentProblem: Boolean,
    /** ISO instant, null when not Premium. */
    val accessEndsAt: String?,
)

/** A feature on the plans screen; [comingSoon] ones are listed but not granted yet. */
data class PlanFeature(val key: String, val title: String, val description: String, val comingSoon: Boolean)

/** The Premium plan; the price is for display, the payment provider charges the real one. */
data class Plan(val priceAmount: Double, val currency: String, val interval: String, val features: List<PlanFeature>)
