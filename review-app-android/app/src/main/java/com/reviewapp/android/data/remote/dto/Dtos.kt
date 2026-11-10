package com.reviewapp.android.data.remote.dto

import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.AchievementCategory
import com.reviewapp.android.domain.model.AchievementTier
import com.reviewapp.android.domain.model.AchievementTierInfo
import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.CollectionDetails
import com.reviewapp.android.domain.model.CollectionItem
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.PlanFeature
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.Recognition
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionItems
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.RecognitionTier
import com.reviewapp.android.domain.model.RecognizedMedia
import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReplyContext
import com.reviewapp.android.domain.model.ReplyPage
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.domain.model.SecurityStatus
import com.reviewapp.android.domain.model.SubscriptionStatus
import com.reviewapp.android.domain.model.SubscriptionSummary
import com.reviewapp.android.domain.model.UnlockedAchievement
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.UserSummary
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.domain.model.mediaKey
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// Request/response shapes of the API (camelCase JSON, enums as ints). Note `externalApiID`.

/** The confirmation message many write endpoints answer with. */
@Serializable
data class MessageResponse(val message: String? = null)

/** `{ error }` from hand-written errors, `{ title, errors }` from automatic model validation. */
@Serializable
data class ErrorResponse(
    val error: String? = null,
    val title: String? = null,
    val errors: Map<String, List<String>>? = null,
    /** Machine readable reason of some errors, e.g. `daily_limit`, `busy`, `not_found`. */
    val code: String? = null,
)

/** A numbered page of results; the defaults make an empty or partial answer read as an empty list. [toPage] maps it to the domain type. */
@Serializable
data class PagedResponse<T>(
    val items: List<T> = emptyList(),
    val totalCount: Int = 0,
    val currentPage: Int = 1,
    val totalPages: Int = 0,
)

fun <T, R> PagedResponse<T>.toPage(transform: (T) -> R) =
    Page(items.map(transform), currentPage, totalPages, totalCount)

// Auth

/** The logged-in user as login and check-auth return it. */
@Serializable
data class UserDto(
    val id: Int,
    val username: String,
    val profilePictureUrl: String? = null,
    val isEmailConfirmed: Boolean = false,
    val isPremium: Boolean = false,
    /** Feature keys the user may use, see `FeatureKeys`. */
    val entitlements: List<String> = emptyList(),
) {
    fun toDomain() = AuthUser(id, username, profilePictureUrl, isEmailConfirmed, isPremium, entitlements.toSet())
}

/** `POST Auth/register`. Does not log in. */
@Serializable
data class RegisterRequest(val username: String, val email: String, val password: String)

/** `POST Auth/login`. Credentials are sent as plain JSON over HTTPS by design, never hashed client-side. */
@Serializable
data class LoginRequest(val email: String, val password: String)

/** Either `user` is set (logged in) or `twoFactorRequired` with a `challengeToken`. */
@Serializable
data class LoginResponse(
    val message: String? = null,
    val user: UserDto? = null,
    val twoFactorRequired: Boolean = false,
    val challengeToken: String? = null,
)

/** The second step of a 2FA login: the challenge token from [LoginResponse] and the emailed code. */
@Serializable
data class TwoFactorLoginRequest(val challengeToken: String, val code: String)

/** Asks for a new 2FA login code for a pending login. */
@Serializable
data class TwoFactorResendRequest(val challengeToken: String)

// Media

/** A movie, series or track. `id` is a key the API builds (the external ID with the media type name), the app identifies a title by `mediaType` and `externalApiID`. Blank strings are read as missing. */
@Serializable
data class MediaDto(
    val id: String = "",
    @SerialName("externalApiID") val externalApiId: String = "",
    val mediaType: Int = 0,
    val title: String = "",
    val releaseDate: String? = null,
    val posterUrl: String? = null,
    val overview: String? = null,
    val creator: String? = null,
) {
    fun toDomain() = Media(
        externalApiId = externalApiId,
        mediaType = MediaType.fromId(mediaType),
        title = title,
        releaseDate = releaseDate?.takeIf { it.isNotBlank() },
        posterUrl = posterUrl?.takeIf { it.isNotBlank() },
        overview = overview.orEmpty(),
        creator = creator.orEmpty(),
    )
}

/** The API only reads `mediaType` and `externalApiID`, but `id` is required too (`"<externalId><TypeName>"`). */
fun Media.toDto() = MediaDto(
    id = externalApiId + mediaType.name,
    externalApiId = externalApiId,
    mediaType = mediaType.id,
    title = title.take(100),
    releaseDate = releaseDate,
    posterUrl = posterUrl,
    overview = overview,
    creator = creator.take(50),
)

// Reviews

/** The community average score and review count of a title. */
@Serializable
data class AverageScoreResponse(val averageScore: Double = 0.0, val reviewCount: Int = 0) {
    fun toDomain() = ScoreStats(averageScore, reviewCount)
}

/** A review in the list on a media's page, with its author. */
@Serializable
data class MediaReviewDto(
    val id: Int,
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val username: String = "",
    val profilePictureUrl: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
    val replyCount: Int = 0,
) {
    fun toDomain() = MediaReview(
        id, score, reviewText, pros, cons, username, profilePictureUrl, createdAt, updatedAt, replyCount,
    )
}

/** A review in the home feed: its author and the reviewed media. */
@Serializable
data class FeedReviewDto(
    val id: Int,
    val username: String = "",
    val profilePictureUrl: String? = null,
    val title: String = "",
    val posterUrl: String? = null,
    val mediaType: Int = 0,
    @SerialName("externalApiID") val externalApiId: String = "",
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val createdAt: String = "",
    val replyCount: Int = 0,
) {
    fun toDomain() = FeedReview(
        id, username, profilePictureUrl, title, posterUrl, MediaType.fromId(mediaType), externalApiId,
        score, reviewText, pros, cons, createdAt, replyCount,
    )
}

/** The user-written part of a review. A missing `visibilityLevel` reads as public here, matching the app's default for a new review. */
@Serializable
data class ReviewDto(
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val visibilityLevel: Int = Visibility.Public.id,
) {
    fun toDraft() = ReviewDraft(
        score = score,
        reviewText = reviewText.orEmpty(),
        pros = pros.orEmpty(),
        cons = cons.orEmpty(),
        visibility = Visibility.fromId(visibilityLevel),
    )
}

fun ReviewDraft.toDto() = ReviewDto(
    score = score,
    reviewText = reviewText.trim().ifEmpty { null },
    pros = pros.trim().ifEmpty { null },
    cons = cons.trim().ifEmpty { null },
    visibilityLevel = visibility.id,
)

/** Create or edit a review: the title and the review (the API finds an existing review by user and title). */
@Serializable
data class ReviewMediaRequest(val mediaDto: MediaDto, val reviewDto: ReviewDto)

/** Whether the current user already reviewed a title, with that review. */
@Serializable
data class ReviewCheckResponse(val hasReviewed: Boolean = false, val reviewData: ReviewDto? = null)

// Users and followers

/** A user's public profile. */
@Serializable
data class UserProfileDto(
    val id: Int,
    val username: String,
    val bio: String? = null,
    val profilePictureUrl: String? = null,
    val createdAt: String = "",
    val followersCount: Int = 0,
    val followingCount: Int = 0,
    val isFollowedByCurrentUser: Boolean = false,
    val isPremium: Boolean = false,
) {
    fun toDomain() = UserProfile(
        id = id,
        username = username,
        bio = bio.orEmpty(),
        profilePictureUrl = profilePictureUrl?.takeIf { it.isNotBlank() },
        joinedAt = createdAt,
        followersCount = followersCount,
        followingCount = followingCount,
        isFollowed = isFollowedByCurrentUser,
        isPremium = isPremium,
    )
}

/** In search results `isFollowedByCurrentUser` is always null. */
@Serializable
data class UserCompactDto(
    val id: Int,
    val username: String,
    val profilePictureUrl: String? = null,
    val isFollowedByCurrentUser: Boolean? = null,
) {
    fun toDomain() = UserSummary(id, username, profilePictureUrl?.takeIf { it.isNotBlank() }, isFollowedByCurrentUser == true)
}

/** `PATCH User/me`: null fields are left out of the JSON (`explicitNulls = false`), so they stay unchanged. */
@Serializable
data class UserUpdateRequest(
    val username: String? = null,
    val bio: String? = null,
    val profilePictureUrl: String? = null,
)

/** A review in a user's profile, it carries the media instead of the author. */
@Serializable
data class ProfileReviewDto(
    val id: Int,
    val title: String = "",
    val posterUrl: String? = null,
    val mediaType: Int = 0,
    @SerialName("externalApiID") val externalApiId: String = "",
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
    val visibilityLevel: Int = Visibility.Public.id,
    val isOwner: Boolean = false,
    val replyCount: Int = 0,
) {
    fun toDomain() = ProfileReview(
        id, title, posterUrl, MediaType.fromId(mediaType), externalApiId, score, reviewText, pros, cons,
        createdAt, updatedAt, Visibility.fromId(visibilityLevel), isOwner, replyCount,
    )
}

// Collections

/** A collection as listed on a profile. A missing `visibilityLevel` reads as private, the API's default. */
@Serializable
data class CollectionDto(
    val id: Int,
    val name: String = "",
    val visibilityLevel: Int = Visibility.Private.id,
    val createdAt: String? = null,
    val mediaCount: Int = 0,
    val isOwner: Boolean = false,
    val isDefault: Boolean = false,
) {
    fun toDomain() = MediaCollection(
        id, name, Visibility.fromId(visibilityLevel), createdAt.orEmpty(), mediaCount, isOwner, isDefault,
    )
}

/** One title in a collection; `dbMediaID` is the API's own media id, needed for reordering. */
@Serializable
data class CollectionMediaDto(
    @SerialName("dbMediaID") val dbMediaId: Int,
    val media: MediaDto,
    val orderIndex: Int = 0,
) {
    fun toDomain() = CollectionItem(dbMediaId, media.toDomain())
}

/** `mediaItems` come sorted by `orderIndex`. */
@Serializable
data class CollectionWithMediasDto(
    val collection: CollectionDto,
    val mediaItems: List<CollectionMediaDto> = emptyList(),
) {
    fun toDomain() = CollectionDetails(collection.toDomain(), mediaItems.map { it.toDomain() })
}

/** `POST Collection`. Names are unique per user. */
@Serializable
data class CreateCollectionRequest(val name: String, val visibilityLevel: Int)

/** `PUT Collection`: a new name and visibility. */
@Serializable
data class UpdateCollectionRequest(
    @SerialName("collectionID") val collectionId: Int,
    val name: String,
    val visibilityLevel: Int,
)

/** The API looks the media up itself (TMDb / Spotify), so only the type and id are needed. */
@Serializable
data class AddMediaRequest(
    val type: Int,
    @SerialName("externalApiID") val externalApiId: String,
)

/** [dbMediaId] is the API's own media id from [CollectionMediaDto], not the external one. */
@Serializable
data class ReorderMediaRequest(
    @SerialName("dbMediaID") val dbMediaId: Int,
    val newOrderIndex: Int,
)

/** One title in one of the caller's collections; the app caches the whole list to show favourite and collection state on cards. */
@Serializable
data class CollectionMembershipDto(
    @SerialName("collectionID") val collectionId: Int,
    val mediaType: Int = 0,
    @SerialName("externalApiID") val externalApiId: String = "",
) {
    val mediaKey: String get() = mediaKey(MediaType.fromId(mediaType), externalApiId)
}

/** `GET Review/{id}`: one review with its author and media. */
@Serializable
data class ReviewDetailDto(
    val id: Int,
    val username: String = "",
    val profilePictureUrl: String? = null,
    val title: String = "",
    val posterUrl: String? = null,
    val mediaType: Int = 0,
    @SerialName("externalApiID") val externalApiId: String = "",
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
    val visibilityLevel: Int = Visibility.Public.id,
    val isOwner: Boolean = false,
    val replyCount: Int = 0,
) {
    fun toDomain() = ReviewDetail(
        review = ProfileReview(
            id, title, posterUrl, MediaType.fromId(mediaType), externalApiId, score, reviewText, pros, cons,
            createdAt, updatedAt, Visibility.fromId(visibilityLevel), isOwner, replyCount,
        ),
        username = username,
        profilePictureUrl = profilePictureUrl?.takeIf { it.isNotBlank() },
    )
}

// Review replies

/** A deleted reply comes as a placeholder: no content, no author, `isOwner` false. */
@Serializable
data class ReplyDto(
    val id: Int,
    @SerialName("parentReplyID") val parentReplyId: Int? = null,
    val content: String? = null,
    val username: String? = null,
    val profilePictureUrl: String? = null,
    val createdAt: String = "",
    val isDeleted: Boolean = false,
    val isOwner: Boolean = false,
    val childCount: Int = 0,
) {
    fun toDomain() = Reply(
        id = id,
        parentReplyId = parentReplyId,
        content = content.orEmpty(),
        username = username,
        profilePictureUrl = profilePictureUrl?.takeIf { it.isNotBlank() },
        createdAt = createdAt,
        isDeleted = isDeleted,
        isOwner = isOwner,
        childCount = childCount,
    )
}

/** Keyset page: send the last item's id as `afterId` for the next one. */
@Serializable
data class ReplyPageDto(val items: List<ReplyDto> = emptyList(), val hasMore: Boolean = false) {
    fun toDomain() = ReplyPage(items.map { it.toDomain() }, hasMore)
}

/** A reply with its layer-2 parent when it's a reply to a reply, for opening a thread at that reply. */
@Serializable
data class ReplyContextDto(
    @SerialName("reviewID") val reviewId: Int,
    val reply: ReplyDto,
    val parentReply: ReplyDto? = null,
) {
    fun toDomain() = ReplyContext(reviewId, reply.toDomain(), parentReply?.toDomain())
}

/** `parentReplyID` null means a reply to the review itself. */
@Serializable
data class CreateReplyRequest(
    @SerialName("reviewID") val reviewId: Int,
    @SerialName("parentReplyID") val parentReplyId: Int?,
    val content: String,
)

// Notifications

/** `type` is the enum's name (`ReviewReply`, ...), `createdAt` an ISO instant in UTC. */
@Serializable
data class NotificationDto(
    val id: Int,
    val type: String = "",
    val title: String = "",
    val message: String = "",
    val isSeen: Boolean = false,
    val createdAt: String = "",
    val actorUsername: String? = null,
    val actorProfilePictureUrl: String? = null,
    @SerialName("reviewID") val reviewId: Int? = null,
    @SerialName("replyID") val replyId: Int? = null,
    val groupCode: String? = null,
) {
    fun toDomain() = AppNotification(
        id = id,
        type = NotificationType.fromName(type),
        title = title,
        message = message,
        isSeen = isSeen,
        createdAt = createdAt,
        actorUsername = actorUsername?.takeIf { it.isNotBlank() },
        actorProfilePictureUrl = actorProfilePictureUrl?.takeIf { it.isNotBlank() },
        reviewId = reviewId,
        replyId = replyId,
        groupCode = groupCode,
    )
}

/** Keyset page: send the last item's id as `afterId` for the next one. */
@Serializable
data class NotificationPageDto(
    val items: List<NotificationDto> = emptyList(),
    val hasMore: Boolean = false,
    val totalCount: Int = 0,
    val unseenCount: Int = 0,
) {
    fun toDomain() = NotificationPage(items.map { it.toDomain() }, hasMore, totalCount, unseenCount)
}

/** The cheap answer of the badge poll. */
@Serializable
data class UnseenCountDto(val count: Int = 0)

/** Marks one notification seen or unseen. */
@Serializable
data class SetSeenRequest(val isSeen: Boolean)

// Achievements

/** One tier of an achievement. */
@Serializable
data class AchievementTierDto(
    val tier: String = "",
    val description: String = "",
    val targetValue: Int = 0,
    val isUnlocked: Boolean = false,
) {
    fun toDomain() = AchievementTierInfo(AchievementTier.fromName(tier), description, targetValue, isUnlocked)
}

/** One card with all of its tiers; `categories` are the flag names (`Movie`, `Review`, ...). */
@Serializable
data class AchievementDto(
    val groupCode: String,
    val title: String = "",
    val iconKey: String = "",
    val categories: List<String> = emptyList(),
    val currentProgress: Int = 0,
    val tiers: List<AchievementTierDto> = emptyList(),
) {
    fun toDomain() = Achievement(
        groupCode = groupCode,
        title = title,
        iconKey = iconKey,
        categories = categories.mapNotNullTo(LinkedHashSet()) { AchievementCategory.fromName(it) },
        currentProgress = currentProgress,
        tiers = tiers.map { it.toDomain() },
    )
}

/** An item of the `X-Unlocked-Achievements` response header (a JSON array). */
@Serializable
data class UnlockedAchievementDto(val groupCode: String = "", val title: String = "", val tier: String = "") {
    fun toDomain() = UnlockedAchievement(groupCode, title, AchievementTier.fromName(tier))
}

// Account security

/** `GET Security/status`. */
@Serializable
data class SecurityStatusDto(
    val email: String = "",
    val isEmailConfirmed: Boolean = false,
    val isTwoFactorEnabled: Boolean = false,
    val pendingEmail: String? = null,
) {
    fun toDomain() = SecurityStatus(email, isEmailConfirmed, isTwoFactorEnabled, pendingEmail?.takeIf { it.isNotBlank() })
}

/** Requests an email change; needs the current password. */
@Serializable
data class ChangeEmailRequest(val newEmail: String, val currentPassword: String)

/** Changes the password; needs the current one. */
@Serializable
data class ChangePasswordRequest(val currentPassword: String, val newPassword: String)

/** The first step of enabling/disabling 2FA. */
@Serializable
data class CurrentPasswordRequest(val currentPassword: String)

/** The emailed code confirming enabling or disabling 2FA. */
@Serializable
data class TwoFactorCodeRequest(val code: String)

// Subscription

/** The current user's subscription. `currentPeriodEnd` is the end of the paid period, `accessEndsAt` when Premium access stops (the period end, or the end of the payment grace period). */
@Serializable
data class SubscriptionStatusDto(
    val status: String = "None",
    val isPremium: Boolean = false,
    val willRenew: Boolean = false,
    val cancelAtPeriodEnd: Boolean = false,
    val hasPaymentProblem: Boolean = false,
    val currentPeriodEnd: String? = null,
    val accessEndsAt: String? = null,
) {
    fun toDomain() = SubscriptionSummary(
        status = SubscriptionStatus.fromName(status),
        isPremium = isPremium,
        willRenew = willRenew,
        cancelAtPeriodEnd = cancelAtPeriodEnd,
        hasPaymentProblem = hasPaymentProblem,
        accessEndsAt = accessEndsAt?.takeIf { it.isNotBlank() },
    )
}

/** One feature on the plans screen. */
@Serializable
data class PlanFeatureDto(
    val key: String,
    val title: String = "",
    val description: String = "",
    val comingSoon: Boolean = false,
)

/** The Premium plan. The price is for display; the payment provider charges the real one. */
@Serializable
data class PlanDto(
    val priceAmount: Double = 0.0,
    val currency: String = "",
    val interval: String = "",
    val features: List<PlanFeatureDto> = emptyList(),
) {
    fun toDomain() = Plan(
        priceAmount = priceAmount,
        currency = currency,
        interval = interval,
        features = features.map { PlanFeature(it.key, it.title, it.description, it.comingSoon) },
    )
}

/** Tells the API where the payment provider's pages send the user back to (the Android return page). */
@Serializable
data class SubscriptionRedirectRequest(val platform: String = "android")

/** A provider page (checkout or customer portal) to open in the browser. */
@Serializable
data class SubscriptionUrlDto(val url: String)

// Camera recognition

/** Whether camera recognition is on and what is left of the daily allowance. */
@Serializable
data class RecognitionAllowanceDto(
    val enabled: Boolean = false,
    val premium: Boolean = false,
    val limit: Int? = null,
    val remaining: Int? = null,
    val resetsAt: String? = null,
) {
    fun toDomain() = RecognitionAllowance(enabled, premium, limit, remaining, resetsAt)
}

/** One suggestion: a normal media item and its tier. */
@Serializable
data class RecognizedMediaDto(
    val media: MediaDto = MediaDto(),
    val tier: String = "",
) {
    fun toDomain() = RecognizedMedia(media.toDomain(), RecognitionTier.fromApi(tier))
}

/** A slice of a stored recognition result, for "Show more". */
@Serializable
data class RecognitionItemsDto(
    val items: List<RecognizedMediaDto> = emptyList(),
    val total: Int = 0,
    val hasMore: Boolean = false,
    val nextOffset: Int = 0,
) {
    fun toDomain() = RecognitionItems(items.map { it.toDomain() }, total, hasMore, nextOffset)
}

/** The answer to a recognition: the outcome, what was seen and the first page of the ranked list. */
@Serializable
data class RecognitionResponseDto(
    val recognitionId: String? = null,
    val outcome: String = "",
    val summary: String = "",
    val items: List<RecognizedMediaDto> = emptyList(),
    val total: Int = 0,
    val hasMore: Boolean = false,
    val nextOffset: Int = 0,
    val incomplete: Boolean = false,
    val allowance: RecognitionAllowanceDto = RecognitionAllowanceDto(),
) {
    fun toDomain() = Recognition(
        recognitionId = recognitionId?.takeIf { it.isNotBlank() },
        outcome = RecognitionOutcome.fromApi(outcome),
        summary = summary,
        firstPage = RecognitionItems(items.map { it.toDomain() }, total, hasMore, nextOffset),
        incomplete = incomplete,
        allowance = allowance.toDomain(),
    )
}
