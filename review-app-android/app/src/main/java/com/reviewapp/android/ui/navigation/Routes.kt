package com.reviewapp.android.ui.navigation

import kotlinx.serialization.Serializable

/*
 * Type-safe Navigation Compose destinations; each screen's ViewModel reads its arguments back from the
 * `SavedStateHandle`. Media types travel as the API's int ids.
 */

/** The Home tab. */
@Serializable
data object HomeRoute

/** The Movies tab (discover). */
@Serializable
data object DiscoverMoviesRoute

/** The Series tab (discover). */
@Serializable
data object DiscoverSeriesRoute

/** The Music tab (discover). */
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

/** The logged-in user's notifications. */
@Serializable
data object NotificationsRoute

/** The logged-in user's account settings (username, email, password, security, sessions). */
@Serializable
data object AccountSettingsRoute

/** Offline mode (Premium): the switch, its state and the offline copy's storage, or the upsell. */
@Serializable
data object OfflineSettingsRoute

/** The changes made offline that the backend hasn't accepted yet, with Discard. */
@Serializable
data object PendingChangesRoute

/** One change in Sync issues (an outbox row, by its [seq]) and the choices to resolve it. */
@Serializable
data class ConflictRoute(val seq: Long)

/** Plans page with the current user's subscription. */
@Serializable
data object PremiumRoute

/** Camera recognition (Premium): a photo in, related media out. */
@Serializable
data object RecognitionRoute

/**
 * Where the payment provider sends the user back: `reviewapp://subscription/{result}` (opened by the API's
 * return page in the browser), [result] being `success`, `cancelled` or `portal`.
 */
@Serializable
data class SubscriptionReturnRoute(val result: String)

/** [username]'s followers, or the users they follow when [showFollowing] is set. */
@Serializable
data class FollowListRoute(val username: String, val showFollowing: Boolean)

/** One collection with its items. */
@Serializable
data class CollectionRoute(val collectionId: Int)

/** Search for users by name. */
@Serializable
data object UserSearchRoute

/** One review with its replies; [replyId] opens the thread at that reply (also a `reviewapp://review/...` deep link). */
@Serializable
data class ReviewRoute(val reviewId: Int, val replyId: Int? = null)

/** Login and registration, pushed on top of the screen that needed a logged-in user. */
@Serializable
data object LoginRoute

/** Media search results for [query]. */
@Serializable
data class SearchRoute(val query: String)

/** A movie, series or album, identified by its media type id and the source's id. */
@Serializable
data class MediaDetailsRoute(val mediaType: Int, val externalApiId: String)

/** The review form (create or edit) for the given media. */
@Serializable
data class WriteReviewRoute(val mediaType: Int, val externalApiId: String)
