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
) {
    val mediaKey: String get() = mediaKey(mediaType, externalApiId)
}

data class MediaCollection(
    val id: Int,
    val name: String,
    val visibility: Visibility,
    val createdAt: String,
    val mediaCount: Int,
    val isOwner: Boolean,
    /** The permanent "Favourites" collection, it can't be renamed, edited or deleted. */
    val isDefault: Boolean,
)

/** A media in a collection; [dbMediaId] is the API's own id, needed for reordering. */
data class CollectionItem(val dbMediaId: Int, val media: Media)

data class CollectionDetails(val collection: MediaCollection, val items: List<CollectionItem>)

/** A single review with its author (the review page). */
data class ReviewDetail(
    val review: ProfileReview,
    val username: String,
    val profilePictureUrl: String?,
)

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

data class ReplyPage(val items: List<Reply>, val hasMore: Boolean)

data class ReplyContext(val reviewId: Int, val reply: Reply, val parentReply: Reply?)
