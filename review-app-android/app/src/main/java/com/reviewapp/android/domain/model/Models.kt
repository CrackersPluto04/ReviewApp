package com.reviewapp.android.domain.model

/** Same values as the API's `MediaType` enum. [pathSegment] is the name used in search/discover routes. */
enum class MediaType(val id: Int, val label: String, val pathSegment: String) {
    Movie(0, "Movie", "movie"),
    Series(1, "Series", "series"),
    Music(2, "Music", "music");

    companion object {
        fun fromId(id: Int): MediaType = entries.firstOrNull { it.id == id } ?: Movie
    }
}

/** Same values as the API's `VisibilityLevel` enum. */
enum class Visibility(val id: Int, val label: String) {
    Public(1, "Public"),
    FollowersOnly(2, "Followers only"),
    Private(0, "Private");

    companion object {
        fun fromId(id: Int): Visibility = entries.firstOrNull { it.id == id } ?: Public
    }
}

data class Media(
    val externalApiId: String,
    val mediaType: MediaType,
    val title: String,
    val releaseDate: String?,
    val posterUrl: String?,
    val overview: String,
    val creator: String,
) {
    val key: String get() = mediaKey(mediaType, externalApiId)
}

fun mediaKey(mediaType: MediaType, externalApiId: String) = "${mediaType.id}:$externalApiId"

data class AuthUser(
    val id: Int,
    val username: String,
    val profilePictureUrl: String?,
    val isEmailConfirmed: Boolean,
)

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

data class Page<T>(
    val items: List<T>,
    val page: Int,
    val totalPages: Int,
    val totalCount: Int,
) {
    val hasMore: Boolean get() = page < totalPages
}
