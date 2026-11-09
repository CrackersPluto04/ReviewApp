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

// The push (POST Sync/push): the queued changes in the order the user made them, each answered on its own

@Serializable
data class SyncOpDto(
    val opId: String,
    val type: String,
    val clientRef: String? = null,
    val dependsOn: List<String>? = null,
    val payload: kotlinx.serialization.json.JsonObject,
)

@Serializable
data class SyncPushRequest(val userId: Int, val ops: List<SyncOpDto>)

/** [entity]: the server's version of what the op touched (a [SyncReviewDto], [SyncCollectionDto] or profile). */
@Serializable
data class SyncOpResultDto(
    val opId: String,
    val status: String,
    val reason: String? = null,
    val entityId: Int? = null,
    val entity: kotlinx.serialization.json.JsonElement? = null,
    val conflictFields: List<String>? = null,
    val previous: Map<String, String?>? = null,
)

@Serializable
data class SyncPushResponse(
    val results: List<SyncOpResultDto> = emptyList(),
    val unlocked: List<UnlockedAchievementDto> = emptyList(),
    val serverTime: String = "",
)

// Op payloads, the shapes of the API's SyncOps.cs. Null members are left out of the JSON.

/** A review or collection: its real id, or the clientRef of the queued op that creates it. */
@Serializable
data class EntityRefDto(val id: Int? = null, val ref: String? = null)

@Serializable
data class MediaKeyDto(val type: Int, val externalApiId: String)

@Serializable
data class ReviewFieldsDto(
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val visibility: Int,
)

@Serializable
data class CollectionFieldsDto(val name: String, val visibility: Int)

/** A field left out is unchanged, an empty string clears it. */
@Serializable
data class ProfileFieldsDto(val bio: String? = null, val profilePictureUrl: String? = null)

@Serializable
data class ReviewCreatePayload(val media: MediaKeyDto, val fields: ReviewFieldsDto, val createdAt: String? = null)

@Serializable
data class ReviewUpdatePayload(
    val review: EntityRefDto,
    val baseVersion: Long? = null,
    val base: ReviewFieldsDto? = null,
    val fields: ReviewFieldsDto,
)

@Serializable
data class ReviewDeletePayload(val review: EntityRefDto, val baseVersion: Long? = null)

@Serializable
data class CollectionCreatePayload(val fields: CollectionFieldsDto)

@Serializable
data class CollectionUpdatePayload(
    val collection: EntityRefDto,
    val baseVersion: Long? = null,
    val base: CollectionFieldsDto? = null,
    val fields: CollectionFieldsDto,
)

@Serializable
data class CollectionDeletePayload(val collection: EntityRefDto)

/** collection.addItem and collection.removeItem. */
@Serializable
data class CollectionItemPayload(val collection: EntityRefDto, val media: MediaKeyDto)

@Serializable
data class CollectionReorderPayload(val collection: EntityRefDto, val items: List<MediaKeyDto>)

@Serializable
data class ProfileUpdatePayload(val fields: ProfileFieldsDto, val base: ProfileFieldsDto? = null)

@Serializable
data class FollowSetPayload(val userId: Int, val follow: Boolean)

@Serializable
data class FollowerRemovePayload(val userId: Int)

@Serializable
data class NotificationSeenPayload(val id: Int, val seen: Boolean)

@Serializable
data class NotificationDeletePayload(val id: Int)

/** Mark all seen / delete all seen, up to the newest notification the device had. */
@Serializable
data class NotificationUpToPayload(val upToId: Int)

@Serializable
data class SyncVisibleDto(val reviewIds: List<Int> = emptyList(), val collectionIds: List<Int> = emptyList())
