package com.reviewapp.android.data.remote.dto

import kotlinx.serialization.Serializable

// The offline sync (api/Sync). Unlike the older endpoints these use `externalApiId` (lower-case d), enums are
// ints and times are ISO UTC instants.

@Serializable
data class SyncMediaDto(
    val dbMediaId: Int = 0,
    val mediaType: Int = 0,
    val externalApiId: String = "",
    val title: String? = null,
    val posterUrl: String? = null,
    val releaseDate: String? = null,
    val creator: String? = null,
)

/** The server's version of one of the user's own reviews; [version] is its rowversion. */
@Serializable
data class SyncReviewDto(
    val id: Int,
    val media: SyncMediaDto,
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val visibility: Int = 1,
    val createdAt: String = "",
    val updatedAt: String = "",
    val version: Long = 0,
)

@Serializable
data class SyncCollectionItemDto(
    val media: SyncMediaDto,
    val orderIndex: Int = 0,
    val addedAt: String = "",
)

@Serializable
data class SyncCollectionDto(
    val id: Int,
    val name: String = "",
    val visibility: Int = 0,
    val isDefault: Boolean = false,
    val createdAt: String = "",
    val updatedAt: String = "",
    val version: Long = 0,
    val items: List<SyncCollectionItemDto> = emptyList(),
)

/** The account part of the first page of a sync run. */
@Serializable
data class SyncAccountDto(
    val userId: Int,
    val username: String = "",
    val bio: String? = null,
    val profilePictureUrl: String? = null,
    val following: List<UserCompactDto> = emptyList(),
    val followers: List<UserCompactDto> = emptyList(),
    val notifications: List<NotificationDto> = emptyList(),
    val unseenNotificationCount: Int = 0,
)

/** `GET Sync/changes`: what changed in the user's own content since `since`, oldest first. */
@Serializable
data class SyncChangesDto(
    val reviews: List<SyncReviewDto> = emptyList(),
    val deletedReviewIds: List<Int> = emptyList(),
    val collections: List<SyncCollectionDto> = emptyList(),
    val deletedCollectionIds: List<Int> = emptyList(),
    val cursor: Long = 0,
    val hasMore: Boolean = false,
    val fullResync: Boolean = false,
    val account: SyncAccountDto? = null,
    val serverTime: String = "",
)

@Serializable
data class SyncVisibleRequest(val reviewIds: List<Int>, val collectionIds: List<Int>)

@Serializable
data class SyncVisibleDto(val reviewIds: List<Int> = emptyList(), val collectionIds: List<Int> = emptyList())
