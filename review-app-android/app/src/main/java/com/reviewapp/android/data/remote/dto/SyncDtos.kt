package com.reviewapp.android.data.remote.dto

import kotlinx.serialization.Serializable

// The offline sync (api/Sync). Unlike the older endpoints these use `externalApiId` (lower-case d), enums are
// ints and times are ISO UTC instants.

/** A title as the sync feed describes it; [dbMediaId] is the API's own media id. */
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

/** One title in a synced collection, with its position. */
@Serializable
data class SyncCollectionItemDto(
    val media: SyncMediaDto,
    val orderIndex: Int = 0,
    val addedAt: String = "",
)

/** The server's version of one of the user's own collections with all of its items; [version] is its rowversion, which moves on every change of the collection or its items. */
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

/** Asks which of the other users' cached reviews and collections are still visible to the user (at most 1000 of each). */
@Serializable
data class SyncVisibleRequest(val reviewIds: List<Int>, val collectionIds: List<Int>)

// The push (POST Sync/push): the queued changes in the order the user made them, each answered on its own

/** One queued change. [opId] is generated on the device and makes a resend safe; [clientRef] names what a create makes, so later ops can point at it; [dependsOn] lists the ops that must have been applied first. The [payload] shape depends on [type] (see the payloads below). */
@Serializable
data class SyncOpDto(
    val opId: String,
    val type: String,
    val clientRef: String? = null,
    val dependsOn: List<String>? = null,
    val payload: kotlinx.serialization.json.JsonObject,
)

/** `POST Sync/push`: the queued changes in the order the user made them (at most 50 per request); [userId] must be the logged-in user. */
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

/** One result per op, the achievements the applied changes unlocked and the server's time. */
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

/** A title in an op payload, addressed by type and external id; the API resolves it. */
@Serializable
data class MediaKeyDto(val type: Int, val externalApiId: String)

/** The editable fields of a review in an op payload. */
@Serializable
data class ReviewFieldsDto(
    val score: Double,
    val reviewText: String? = null,
    val pros: String? = null,
    val cons: String? = null,
    val visibility: Int,
)

/** The editable fields of a collection in an op payload. */
@Serializable
data class CollectionFieldsDto(val name: String, val visibility: Int)

/** A field left out is unchanged, an empty string clears it. */
@Serializable
data class ProfileFieldsDto(val bio: String? = null, val profilePictureUrl: String? = null)

/** `review.create`; [createdAt] is the device's time, which the API accepts within a bounded range. */
@Serializable
data class ReviewCreatePayload(val media: MediaKeyDto, val fields: ReviewFieldsDto, val createdAt: String? = null)

/** `review.update`: [base] holds the values the device last saw and [baseVersion] their rowversion, so the API can merge field by field instead of overwriting. */
@Serializable
data class ReviewUpdatePayload(
    val review: EntityRefDto,
    val baseVersion: Long? = null,
    val base: ReviewFieldsDto? = null,
    val fields: ReviewFieldsDto,
)

/** `review.delete`; the API refuses it with `modified_since` when the review was edited elsewhere after [baseVersion]. */
@Serializable
data class ReviewDeletePayload(val review: EntityRefDto, val baseVersion: Long? = null)

/** `collection.create`. */
@Serializable
data class CollectionCreatePayload(val fields: CollectionFieldsDto)

/** `collection.update`, merged against [base] like a review update. */
@Serializable
data class CollectionUpdatePayload(
    val collection: EntityRefDto,
    val baseVersion: Long? = null,
    val base: CollectionFieldsDto? = null,
    val fields: CollectionFieldsDto,
)

/** `collection.delete`. */
@Serializable
data class CollectionDeletePayload(val collection: EntityRefDto)

/** collection.addItem and collection.removeItem. */
@Serializable
data class CollectionItemPayload(val collection: EntityRefDto, val media: MediaKeyDto)

/** `collection.reorder`: the whole new order of the items, not a single move. */
@Serializable
data class CollectionReorderPayload(val collection: EntityRefDto, val items: List<MediaKeyDto>)

/** `profile.update`: the latest change wins, [base] lets the API report which values it overwrote. */
@Serializable
data class ProfileUpdatePayload(val fields: ProfileFieldsDto, val base: ProfileFieldsDto? = null)

/** `follow.set`: the wanted state, so a repeated op is harmless. */
@Serializable
data class FollowSetPayload(val userId: Int, val follow: Boolean)

/** `follower.remove`. */
@Serializable
data class FollowerRemovePayload(val userId: Int)

/** `notification.seen`. */
@Serializable
data class NotificationSeenPayload(val id: Int, val seen: Boolean)

/** `notification.delete`. */
@Serializable
data class NotificationDeletePayload(val id: Int)

/** Mark all seen / delete all seen, up to the newest notification the device had. */
@Serializable
data class NotificationUpToPayload(val upToId: Int)

/** The ids that are still visible; the device drops the rest from its cache. */
@Serializable
data class SyncVisibleDto(val reviewIds: List<Int> = emptyList(), val collectionIds: List<Int> = emptyList())
