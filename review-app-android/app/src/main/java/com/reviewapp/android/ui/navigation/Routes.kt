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

/** Temporary profile tab (theme + logout) until the profile pages are built. */
@Serializable
data object AccountRoute

@Serializable
data object LoginRoute

@Serializable
data class SearchRoute(val query: String)

@Serializable
data class MediaDetailsRoute(val mediaType: Int, val externalApiId: String)

@Serializable
data class WriteReviewRoute(val mediaType: Int, val externalApiId: String)
