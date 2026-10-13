package com.reviewapp.android.data.remote.dto

import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.domain.model.Visibility
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
