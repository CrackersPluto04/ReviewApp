package com.reviewapp.android.data.local.offline

import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey

/*
 * The offline copy of one user's data. Conventions:
 * - Ids are the backend's; things created offline get negative temporary ids until the sync maps them (id_map).
 * - Media types and visibilities are the API's ints; server times are its ISO UTC strings, device times epoch millis.
 * - `version` is the backend's rowversion the row was last seen at (null = never on the backend), the base of the
 *   next offline edit; `syncState` drives the "waiting to sync" markers (see [SyncState]).
 */

/** The "waiting to sync" marker of a row. */
object SyncState {
    /** Matches the backend. */
    const val SYNCED = 0

    /** A change of the row waits in the outbox. */
    const val PENDING = 1


    /** In conflict or rejected: shown in Sync issues until the user decides. */
    const val ISSUE = 2
}

/** The user's own reviews (every visibility). One per media, like on the backend. */
@Entity(
    tableName = "reviews",
    indices = [Index(value = ["mediaType", "externalApiId"], unique = true)],
)
data class ReviewEntity(
    @PrimaryKey val id: Int,
    val mediaType: Int,
    val externalApiId: String,
    val score: Double,
    val reviewText: String?,
    val pros: String?,
    val cons: String?,
    val visibility: Int,
    val createdAt: String,
    val updatedAt: String,
    val version: Long?,
    val syncState: Int = SyncState.SYNCED,
    /** Replies as last seen online (the changes feed doesn't carry them; kept across pulls). */
    val replyCount: Int = 0,
)

/** The user's own collections, Favourites included. */
@Entity(tableName = "collections")
data class CollectionEntity(
    @PrimaryKey val id: Int,
    val name: String,
    val visibility: Int,
    val isDefault: Boolean,
    val createdAt: String,
    val updatedAt: String,
    val version: Long?,
    val syncState: Int = SyncState.SYNCED,
)

/** One title in one of the user's collections, with its position; the media details are in [MediaEntity]. */
@Entity(
    tableName = "collection_items",
    primaryKeys = ["collectionId", "mediaType", "externalApiId"],
    foreignKeys = [
        ForeignKey(
            entity = CollectionEntity::class,
            parentColumns = ["id"],
            childColumns = ["collectionId"],
            onDelete = ForeignKey.CASCADE,
            onUpdate = ForeignKey.CASCADE,
        ),
    ],
    indices = [Index("mediaType", "externalApiId")],
)
data class CollectionItemEntity(
    val collectionId: Int,
    val mediaType: Int,
    val externalApiId: String,
    val orderIndex: Int,
    val addedAt: String,
    val syncState: Int = SyncState.SYNCED,
)

/**
 * Media the device knows: the ones in the user's own content (never evicted while referenced) and the ones the
 * user opened (kept for the offline search, the oldest unreferenced ones dropped first). The details screen's
 * data itself is a snapshot of `GET Media/media/...`.
 */
@Entity(tableName = "media", primaryKeys = ["mediaType", "externalApiId"], indices = [Index("lastUsedAt")])
data class MediaEntity(
    val mediaType: Int,
    val externalApiId: String,
    /** The backend's media row id, when known (the sync sends it with the user's content). */
    val dbMediaId: Int?,
    val title: String?,
    val posterUrl: String?,
    val releaseDate: String?,
    val creator: String?,
    val lastUsedAt: Long,
)

/** The logged-in user's own profile (a single row; the page with its counts is a snapshot). */
@Entity(tableName = "profile")
data class ProfileEntity(
    @PrimaryKey val userId: Int,
    val username: String,
    val bio: String?,
    val profilePictureUrl: String?,
    val syncState: Int = SyncState.SYNCED,
)

/** Other users the device knows (the follow lists): follows work offline only on these. */
@Entity(tableName = "known_users", indices = [Index("username"), Index("lastUsedAt")])
data class KnownUserEntity(
    @PrimaryKey val userId: Int,
    val username: String,
    val profilePictureUrl: String?,
    val lastUsedAt: Long,
)

/** Who follows whom, for the logged-in user's two lists (one side is always the user). */
@Entity(tableName = "follows", primaryKeys = ["followerId", "followingId"], indices = [Index("followingId")])
data class FollowEntity(
    val followerId: Int,
    val followingId: Int,
    val syncState: Int = SyncState.SYNCED,
)

/**
 * The user's newest notifications as last loaded; [deleted] hides one whose delete is still waiting. Serializable:
 * it is its own shadow while a change of it waits.
 */
@kotlinx.serialization.Serializable
@Entity(tableName = "notifications", indices = [Index("createdAt")])
data class NotificationEntity(
    @PrimaryKey val id: Int,
    val type: String,
    val title: String,
    val message: String,
    val isSeen: Boolean,
    val createdAt: String,
    val actorUsername: String?,
    val actorProfilePictureUrl: String?,
    val reviewId: Int?,
    val replyId: Int?,
    val groupCode: String?,
    val deleted: Boolean = false,
    val syncState: Int = SyncState.SYNCED,
)

/** A user's achievements page as last loaded (read only offline). */
@Entity(tableName = "achievements_json")
data class AchievementsJsonEntity(
    @PrimaryKey val username: String,
    val json: String,
    val savedAt: Long,
)

/**
 * The last answer of a GET request ([key] = its path and query), served when the request fails offline: lists,
 * details and profiles as last seen. [reviewIds] / [collectionIds] (comma-separated) are the reviews and
 * collections it shows, so a snapshot with content its owner has hidden since can be dropped.
 */
@Entity(tableName = "snapshots", indices = [Index("lastUsedAt")])
data class SnapshotEntity(
    @PrimaryKey val key: String,
    val json: String,
    val savedAt: Long,
    val lastUsedAt: Long,
    val sizeBytes: Long,
    val reviewIds: String = "",
    val collectionIds: String = "",
)

/**
 * The backend's last known state of an entity with local changes ([json] = its sync DTO, [version]), so a pending
 * change can be discarded or rebased, and the three-way merge has its base.
 */
@Entity(tableName = "server_shadow", primaryKeys = ["entityType", "entityId"])
data class ServerShadowEntity(
    val entityType: Int,
    val entityId: Int,
    val json: String,
    val version: Long?,
)

/** Entity kinds of the outbox, the shadow and the id map. */
object EntityType {
    const val REVIEW = 0
    const val COLLECTION = 1
    const val PROFILE = 2
    /** The user following someone (entityId = the other user). */
    const val FOLLOW = 3
    const val NOTIFICATION = 4
    /** Someone following the user, removed by the user (entityId = the follower). */
    const val FOLLOWER = 5
}

/** The state of a queued change. */
object OutboxState {
    /** Waiting to be sent (or to be sent again after a failure, see `nextAttemptAt`). */
    const val PENDING = 0


    /** Sent and not answered yet; back to pending when the app starts again. */
    const val IN_FLIGHT = 1

    /** The backend found the change in conflict with another one; it waits in Sync issues for the user's decision. */
    const val CONFLICT = 2

    /** The backend refused the change; it waits in Sync issues. */
    const val REJECTED = 3
}

/**
 * One change made offline, in the order the user made it ([seq]). [opId] is its identity on the backend (sent again
 * after a lost answer, it is recognised); [payloadJson] the op's payload as the push endpoint takes it.
 */
@Entity(
    tableName = "outbox",
    indices = [Index(value = ["opId"], unique = true), Index("entityType", "entityId"), Index("state")],
)
data class OutboxEntity(
    @PrimaryKey(autoGenerate = true) val seq: Long = 0,
    val opId: String,
    val type: String,
    val entityType: Int,
    /** The local id the change is about (negative while only the device knows it), or null (mark all seen). */
    val entityId: Int?,
    /** Set on creates: the backend's ClientRef, so later ops can point at the not yet known id. */
    val clientRef: String?,
    /** Comma-separated opIds this op waits for. */
    val dependsOn: String?,
    val payloadJson: String,
    val state: Int = OutboxState.PENDING,
    val attempts: Int = 0,
    val nextAttemptAt: Long = 0,
    val lastReason: String? = null,
    /** The backend's answer of a conflict / rejection (with its version of the entity), for Sync issues. */
    val resultJson: String? = null,
    val createdAt: Long,
)

/** Temporary (negative) ids of offline creations and the backend's ids they got. */
@Entity(tableName = "id_map", primaryKeys = ["entityType", "tempId"])
data class IdMapEntity(
    val entityType: Int,
    val tempId: Int,
    val realId: Int,
)

/** Unsaved form text (review, collection dialogs), kept when the app is killed. */
@Entity(tableName = "drafts")
data class DraftEntity(
    @PrimaryKey val key: String,
    val json: String,
    val updatedAt: Long,
)

/** Achievements unlocked by a background sync, shown on the next app open. */
@Entity(tableName = "pending_unlocks")
data class PendingUnlockEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val json: String,
    val createdAt: Long,
)

/** Small key/value facts about the local copy (sync cursor, last finished sync, download progress). */
@Entity(tableName = "sync_meta")
data class SyncMetaEntity(
    @PrimaryKey val key: String,
    val value: String,
)

/** The [SyncMetaEntity] keys. */
object SyncMetaKeys {
    /** The changes feed cursor of the next pull. */
    const val CURSOR = "cursor"
    /** The server time of the last finished sync (ISO UTC), sent as `syncedAt`. */
    const val LAST_SYNCED_SERVER_TIME = "last_synced_server_time"
    /** Device time (epoch millis) of the last finished sync, for "Last synced ..." texts. */
    const val LAST_SYNCED_AT = "last_synced_at"
    /** "true" once the first full download finished: until then the mode is Preparing. */
    const val PREPARED = "prepared"
    /** The unseen notification count as last synced. */
    const val UNSEEN_COUNT = "unseen_count"
    /** Device time (epoch millis) when "Make available offline" last finished. */
    const val LIBRARY_DOWNLOADED_AT = "library_downloaded_at"
}
