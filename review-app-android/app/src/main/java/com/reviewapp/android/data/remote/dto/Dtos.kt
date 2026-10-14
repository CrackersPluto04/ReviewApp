package com.reviewapp.android.data.remote.dto

import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.CollectionDetails
import com.reviewapp.android.domain.model.CollectionItem
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReplyContext
import com.reviewapp.android.domain.model.ReplyPage
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.UserSummary
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.domain.model.mediaKey
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// Request/response shapes of the API (camelCase JSON, enums as ints). Note `externalApiID`.

@Serializable
data class MessageResponse(val message: String? = null)

/** `{ error }` from hand-written errors, `{ title, errors }` from automatic model validation. */
@Serializable
data class ErrorResponse(
    val error: String? = null,
    val title: String? = null,
    val errors: Map<String, List<String>>? = null,
)

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

@Serializable
data class UserDto(
    val id: Int,
    val username: String,
    val profilePictureUrl: String? = null,
    val isEmailConfirmed: Boolean = false,
) {
    fun toDomain() = AuthUser(id, username, profilePictureUrl, isEmailConfirmed)
}

@Serializable
data class RegisterRequest(val username: String, val email: String, val password: String)

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

@Serializable
data class TwoFactorLoginRequest(val challengeToken: String, val code: String)

@Serializable
data class TwoFactorResendRequest(val challengeToken: String)

// Media

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

@Serializable
data class AverageScoreResponse(val averageScore: Double = 0.0, val reviewCount: Int = 0) {
    fun toDomain() = ScoreStats(averageScore, reviewCount)
}

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

@Serializable
data class ReviewMediaRequest(val mediaDto: MediaDto, val reviewDto: ReviewDto)

@Serializable
data class ReviewCheckResponse(val hasReviewed: Boolean = false, val reviewData: ReviewDto? = null)

// Users and followers

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

@Serializable
data class CreateCollectionRequest(val name: String, val visibilityLevel: Int)

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
