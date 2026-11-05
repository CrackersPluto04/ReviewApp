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
    const val SYNCED = 0
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
 * Media the device knows: the ones in the user's own content (essential, never evicted) and the ones the user
 * opened (cache, least recently used first out). [detailsJson] is the details screen's data when it was opened.
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
    val detailsJson: String?,
    val lastUsedAt: Long,
    val sizeBytes: Long = 0,
)

/** The logged-in user's own profile (a single row). */
@Entity(tableName = "profile")
data class ProfileEntity(
    @PrimaryKey val userId: Int,
    val username: String,
    val bio: String?,
    val profilePictureUrl: String?,
    /** The profile page's data (counts, badge) as last loaded. */
    val profileJson: String?,
    val syncState: Int = SyncState.SYNCED,
)

/** Other users the device knows (follow lists, opened profiles): follows work offline only on these. */
@Entity(tableName = "known_users", indices = [Index(value = ["username"], unique = true), Index("lastUsedAt")])
data class KnownUserEntity(
    @PrimaryKey val userId: Int,
    val username: String,
    val profilePictureUrl: String?,
    val profileJson: String?,
    val lastUsedAt: Long,
)

/** Who follows whom, for the logged-in user's two lists (one side is always the user). */
@Entity(tableName = "follows", primaryKeys = ["followerId", "followingId"], indices = [Index("followingId")])
data class FollowEntity(
    val followerId: Int,
    val followingId: Int,
    val syncState: Int = SyncState.SYNCED,
)

/** The user's newest notifications as last loaded; [deleted] hides one whose delete is still waiting. */
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

/** The last loaded state of a list screen (home, discover with its filters, a profile's reviews, ...). */
@Entity(tableName = "snapshots", indices = [Index("lastUsedAt")])
data class SnapshotEntity(
    @PrimaryKey val key: String,
    val json: String,
    val savedAt: Long,
    val lastUsedAt: Long,
    val sizeBytes: Long,
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
    const val FOLLOW = 3
    const val NOTIFICATION = 4
}

/** The state of a queued change. */
object OutboxState {
    const val PENDING = 0
    /** Sent and not answered yet; back to pending when the app starts again. */
    const val IN_FLIGHT = 1
    const val CONFLICT = 2
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
    val baseVersion: Long?,
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
}
