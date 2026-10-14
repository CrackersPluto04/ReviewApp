package com.reviewapp.android.ui.navigation

import kotlinx.serialization.Serializable

// Type-safe Navigation Compose destinations. Media types are passed as the API's int ids.

@Serializable
data object HomeRoute

@Serializable
data object DiscoverMoviesRoute

@Serializable
data object DiscoverSeriesRoute

@Serializable
data object DiscoverMusicRoute

/** The Profile tab: the logged-in user's profile, or a login prompt. */
@Serializable
data object MyProfileRoute

/**
 * Anyone's profile (also the current user's when opened from a review or a list). [tab] is a
 * `ProfileTab` name to open on, e.g. the achievements from a notification.
 */
@Serializable
data class ProfileRoute(val username: String, val tab: String? = null)

@Serializable
data object NotificationsRoute

@Serializable
data class FollowListRoute(val username: String, val showFollowing: Boolean)

@Serializable
data class CollectionRoute(val collectionId: Int)

@Serializable
data object UserSearchRoute

/** One review with its replies; [replyId] opens the thread at that reply (also a `reviewapp://review/...` deep link). */
@Serializable
data class ReviewRoute(val reviewId: Int, val replyId: Int? = null)

@Serializable
data object LoginRoute

@Serializable
data class SearchRoute(val query: String)

@Serializable
data class MediaDetailsRoute(val mediaType: Int, val externalApiId: String)

@Serializable
data class WriteReviewRoute(val mediaType: Int, val externalApiId: String)
