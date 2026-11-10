package com.reviewapp.android.data.local.offline

import androidx.room.Dao
import androidx.room.Embedded
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow

/** Reads and writes the small facts about the copy (see [SyncMetaKeys]). */
@Dao
interface SyncMetaDao {
    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    suspend fun get(key: String): String?

    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    fun observe(key: String): Flow<String?>

    @Upsert
    suspend fun put(entry: SyncMetaEntity)

    suspend fun put(key: String, value: String) = put(SyncMetaEntity(key, value))

    @Query("DELETE FROM sync_meta WHERE `key` = :key")
    suspend fun remove(key: String)
}

/** The queue of changes made offline, in the order the user made them. The states are in [OutboxState]. */
@Dao
interface OutboxDao {
    /** Changes the backend hasn't accepted yet (waiting, being sent, or in Sync issues). */
    @Query("SELECT COUNT(*) FROM outbox")
    fun observeCount(): Flow<Int>

    @Query("SELECT COUNT(*) FROM outbox")
    suspend fun count(): Int

    /** Changes in Sync issues (conflicts and rejections waiting for the user). */
    @Query("SELECT COUNT(*) FROM outbox WHERE state IN (2, 3)")
    fun observeIssueCount(): Flow<Int>

    /** The local ids of entities of [entityType] with a pending change. */
    @Query("SELECT DISTINCT entityId FROM outbox WHERE entityType = :entityType AND entityId IS NOT NULL")
    suspend fun pendingIds(entityType: Int): List<Int>

    /** Pending changes of [entityType] that aren't about one entity (mark all seen, delete all seen). */
    @Query("SELECT COUNT(*) FROM outbox WHERE entityType = :entityType AND entityId IS NULL")
    suspend fun bulkCount(entityType: Int): Int

    @Query("SELECT COUNT(*) FROM outbox WHERE entityType = :entityType")
    suspend fun countOf(entityType: Int): Int

    /** Every queued change in the order the user made them. */
    @Query("SELECT * FROM outbox ORDER BY seq")
    suspend fun all(): List<OutboxEntity>

    @Query("SELECT * FROM outbox ORDER BY seq")
    fun observeAll(): Flow<List<OutboxEntity>>

    @Insert
    suspend fun insert(op: OutboxEntity): Long

    @androidx.room.Update
    suspend fun update(ops: List<OutboxEntity>)

    @Query("DELETE FROM outbox WHERE seq IN (:seqs)")
    suspend fun delete(seqs: List<Long>)

    @Query("UPDATE outbox SET state = :to WHERE state = :from")
    suspend fun moveState(from: Int, to: Int)

    /** After a create got its real id: the later changes of the entity point at it. */
    @Query("UPDATE outbox SET entityId = :realId WHERE entityType = :entityType AND entityId = :tempId")
    suspend fun remap(entityType: Int, tempId: Int, realId: Int)

    @Query("SELECT MIN(nextAttemptAt) FROM outbox WHERE state = 0")
    suspend fun nextAttemptAt(): Long?

    /** Changes still to send (not the ones in Sync issues). */
    @Query("SELECT COUNT(*) FROM outbox WHERE state IN (0, 1)")
    suspend fun countToSend(): Int
}

/** Maps the temporary (negative) ids of things created offline to the ids the backend gave them. */
@Dao
interface IdMapDao {
    @Upsert
    suspend fun put(entry: IdMapEntity)

    @Query("SELECT realId FROM id_map WHERE entityType = :entityType AND tempId = :tempId")
    suspend fun realId(entityType: Int, tempId: Int): Int?
}

/** Unsaved form text, kept so it survives the app being killed. */
@Dao
interface DraftDao {
    @Query("SELECT * FROM drafts WHERE `key` = :key")
    suspend fun get(key: String): DraftEntity?

    @Upsert
    suspend fun put(draft: DraftEntity)

    @Query("DELETE FROM drafts WHERE `key` = :key")
    suspend fun delete(key: String)
}

/** Achievements unlocked while the app was in the background, shown on the next open. */
@Dao
interface PendingUnlockDao {
    @Insert
    suspend fun insert(unlock: PendingUnlockEntity)

    @Query("SELECT * FROM pending_unlocks ORDER BY id")
    suspend fun all(): List<PendingUnlockEntity>

    @Query("DELETE FROM pending_unlocks")
    suspend fun clear()
}

/** A review with the title and poster of its media. */
data class LocalReview(
    @Embedded val review: ReviewEntity,
    val title: String?,
    val posterUrl: String?,
)

/** The user's own reviews. A review is joined with its media for the title and poster. */
@Dao
interface ReviewDao {
    @Upsert
    suspend fun upsert(reviews: List<ReviewEntity>)

    @Query("DELETE FROM reviews WHERE id IN (:ids)")
    suspend fun delete(ids: List<Int>)

    @Query("SELECT id FROM reviews")
    suspend fun ids(): List<Int>

    @Query(
        "SELECT r.*, m.title AS title, m.posterUrl AS posterUrl FROM reviews r " +
            "LEFT JOIN media m ON m.mediaType = r.mediaType AND m.externalApiId = r.externalApiId",
    )
    suspend fun all(): List<LocalReview>

    @Query(
        "SELECT r.*, m.title AS title, m.posterUrl AS posterUrl FROM reviews r " +
            "LEFT JOIN media m ON m.mediaType = r.mediaType AND m.externalApiId = r.externalApiId WHERE r.id = :id",
    )
    suspend fun byId(id: Int): LocalReview?

    @Query("SELECT * FROM reviews WHERE mediaType = :mediaType AND externalApiId = :externalApiId")
    suspend fun byMedia(mediaType: Int, externalApiId: String): ReviewEntity?

    @Query("SELECT id, replyCount FROM reviews WHERE id IN (:ids)")
    suspend fun replyCounts(ids: List<Int>): List<ReplyCountRow>

    @Query("UPDATE reviews SET replyCount = :replyCount WHERE id = :id")
    suspend fun setReplyCount(id: Int, replyCount: Int)

    @Query("SELECT * FROM reviews WHERE id = :id")
    suspend fun entity(id: Int): ReviewEntity?

    @Query("UPDATE reviews SET syncState = :state WHERE id = :id")
    suspend fun setSyncState(id: Int, state: Int)

    /** A review created offline got its real id. */
    @Query("UPDATE reviews SET id = :realId WHERE id = :tempId")
    suspend fun remapId(tempId: Int, realId: Int)
}

data class ReplyCountRow(val id: Int, val replyCount: Int)

/** A collection with its number of items. */
data class LocalCollection(
    @Embedded val collection: CollectionEntity,
    val mediaCount: Int,
)

/** A collection item with its media. */
data class LocalCollectionItem(
    @Embedded val item: CollectionItemEntity,
    val dbMediaId: Int?,
    val title: String?,
    val posterUrl: String?,
    val releaseDate: String?,
    val creator: String?,
)

/** The user's own collections and their items. */
@Dao
interface CollectionDao {
    @Upsert
    suspend fun upsert(collections: List<CollectionEntity>)

    @Query("DELETE FROM collections WHERE id IN (:ids)")
    suspend fun delete(ids: List<Int>)

    @Query("SELECT id FROM collections")
    suspend fun ids(): List<Int>

    @Query("SELECT c.*, (SELECT COUNT(*) FROM collection_items i WHERE i.collectionId = c.id) AS mediaCount FROM collections c")
    suspend fun all(): List<LocalCollection>

    @Query("SELECT c.*, (SELECT COUNT(*) FROM collection_items i WHERE i.collectionId = c.id) AS mediaCount FROM collections c WHERE c.id = :id")
    suspend fun byId(id: Int): LocalCollection?

    @Query(
        "SELECT i.*, m.dbMediaId AS dbMediaId, m.title AS title, m.posterUrl AS posterUrl, m.releaseDate AS releaseDate, " +
            "m.creator AS creator FROM collection_items i " +
            "LEFT JOIN media m ON m.mediaType = i.mediaType AND m.externalApiId = i.externalApiId " +
            "WHERE i.collectionId = :collectionId ORDER BY i.orderIndex",
    )
    suspend fun items(collectionId: Int): List<LocalCollectionItem>

    @Query("SELECT * FROM collection_items")
    suspend fun allItems(): List<CollectionItemEntity>

    @Query("DELETE FROM collection_items WHERE collectionId = :collectionId")
    suspend fun deleteItems(collectionId: Int)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertItems(items: List<CollectionItemEntity>)

    @Transaction
    suspend fun replaceItems(collectionId: Int, items: List<CollectionItemEntity>) {
        deleteItems(collectionId)
        if (items.isNotEmpty()) insertItems(items)
    }

    @Query("SELECT * FROM collections WHERE id = :id")
    suspend fun entity(id: Int): CollectionEntity?

    @Query("SELECT * FROM collection_items WHERE collectionId = :collectionId ORDER BY orderIndex")
    suspend fun itemsOf(collectionId: Int): List<CollectionItemEntity>

    @Query("DELETE FROM collection_items WHERE collectionId = :collectionId AND mediaType = :mediaType AND externalApiId = :externalApiId")
    suspend fun deleteItem(collectionId: Int, mediaType: Int, externalApiId: String)

    @Query("UPDATE collections SET syncState = :state WHERE id = :id")
    suspend fun setSyncState(id: Int, state: Int)

    @Query("UPDATE collection_items SET syncState = :state WHERE collectionId = :collectionId")
    suspend fun setItemsSyncState(collectionId: Int, state: Int)

    /** A collection created offline got its real id (its items follow through the foreign key). */
    @Query("UPDATE collections SET id = :realId WHERE id = :tempId")
    suspend fun remapId(tempId: Int, realId: Int)
}

/** The media the device knows. Those in the user's own reviews and collections are never pruned. */
@Dao
interface MediaDao {
    @Upsert
    suspend fun upsert(media: List<MediaEntity>)

    @Query("SELECT * FROM media")
    suspend fun all(): List<MediaEntity>

    @Query("SELECT * FROM media WHERE mediaType = :mediaType AND externalApiId = :externalApiId")
    suspend fun get(mediaType: Int, externalApiId: String): MediaEntity?

    /** The media in the user's own reviews and collections (their posters are kept on the device). */
    @Query(
        "SELECT * FROM media m WHERE EXISTS (SELECT 1 FROM reviews r WHERE r.mediaType = m.mediaType AND r.externalApiId = m.externalApiId) " +
            "OR EXISTS (SELECT 1 FROM collection_items i WHERE i.mediaType = m.mediaType AND i.externalApiId = m.externalApiId)",
    )
    suspend fun ownContentMedia(): List<MediaEntity>

    /** Keeps the [keep] most recently used media that no own review or collection refers to, drops the rest. */
    @Query(
        "DELETE FROM media WHERE rowid IN (SELECT m.rowid FROM media m " +
            "WHERE NOT EXISTS (SELECT 1 FROM reviews r WHERE r.mediaType = m.mediaType AND r.externalApiId = m.externalApiId) " +
            "AND NOT EXISTS (SELECT 1 FROM collection_items i WHERE i.mediaType = m.mediaType AND i.externalApiId = m.externalApiId) " +
            "ORDER BY m.lastUsedAt DESC LIMIT -1 OFFSET :keep)",
    )
    suspend fun pruneUnreferenced(keep: Int)

    @Query(
        "DELETE FROM media WHERE NOT EXISTS (SELECT 1 FROM reviews r WHERE r.mediaType = media.mediaType AND r.externalApiId = media.externalApiId) " +
            "AND NOT EXISTS (SELECT 1 FROM collection_items i WHERE i.mediaType = media.mediaType AND i.externalApiId = media.externalApiId)",
    )
    suspend fun deleteUnreferenced()
}

/** The logged-in user's own profile row. */
@Dao
interface ProfileDao {
    @Upsert
    suspend fun upsert(profile: ProfileEntity)

    @Query("SELECT * FROM profile WHERE userId = :userId")
    suspend fun get(userId: Int): ProfileEntity?
}

/** A user of the follow lists with whether the logged-in user follows them. */
data class LocalFollowUser(
    val userId: Int,
    val username: String,
    val profilePictureUrl: String?,
    val isFollowed: Boolean,
)

/** Who follows whom for the logged-in user's two lists. Only users in `known_users` can be listed. */
@Dao
interface FollowDao {
    @Query(
        "SELECT u.userId, u.username, u.profilePictureUrl, 1 AS isFollowed FROM follows f " +
            "JOIN known_users u ON u.userId = f.followingId WHERE f.followerId = :me ORDER BY u.username COLLATE NOCASE",
    )
    suspend fun following(me: Int): List<LocalFollowUser>

    @Query(
        "SELECT u.userId, u.username, u.profilePictureUrl, " +
            "EXISTS (SELECT 1 FROM follows b WHERE b.followerId = :me AND b.followingId = f.followerId) AS isFollowed " +
            "FROM follows f JOIN known_users u ON u.userId = f.followerId WHERE f.followingId = :me ORDER BY u.username COLLATE NOCASE",
    )
    suspend fun followers(me: Int): List<LocalFollowUser>

    @Query("SELECT COUNT(*) FROM follows WHERE followerId = :me")
    suspend fun followingCount(me: Int): Int

    @Query("SELECT COUNT(*) FROM follows WHERE followingId = :me")
    suspend fun followersCount(me: Int): Int

    @Query("DELETE FROM follows WHERE followerId NOT IN (:keepUserIds) AND followingId NOT IN (:keepUserIds)")
    suspend fun deleteAllExcept(keepUserIds: List<Int>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(follows: List<FollowEntity>)

    @Upsert
    suspend fun upsertUsers(users: List<KnownUserEntity>)

    @Query("SELECT * FROM follows WHERE followerId = :followerId AND followingId = :followingId")
    suspend fun get(followerId: Int, followingId: Int): FollowEntity?

    @Query("SELECT * FROM known_users")
    suspend fun knownUsers(): List<KnownUserEntity>

    @Query("DELETE FROM follows WHERE followerId = :followerId AND followingId = :followingId")
    suspend fun delete(followerId: Int, followingId: Int)

    /** Replaces the follow rows, keeping the ones of users with a pending change. */
    @Transaction
    suspend fun replace(follows: List<FollowEntity>, keepUserIds: Set<Int>, users: List<KnownUserEntity>) {
        // NOT IN () would match nothing, so an id that is never a user stands in for an empty set
        deleteAllExcept(keepUserIds.ifEmpty { setOf(0) }.toList())
        upsertUsers(users)
        if (follows.isNotEmpty()) insert(follows)
    }
}

/** The user's newest notifications. A deleted one stays as a hidden row until its delete is sent. */
@Dao
interface NotificationDao {
    @Query("SELECT * FROM notifications WHERE deleted = 0")
    suspend fun all(): List<NotificationEntity>

    @Query("SELECT COUNT(*) FROM notifications WHERE deleted = 0 AND isSeen = 0")
    suspend fun unseenCount(): Int

    @Query("DELETE FROM notifications WHERE id NOT IN (:keepIds)")
    suspend fun deleteAllExcept(keepIds: List<Int>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(notifications: List<NotificationEntity>)

    @Transaction
    suspend fun replace(notifications: List<NotificationEntity>, keepIds: Set<Int>) {
        deleteAllExcept(keepIds.ifEmpty { setOf(0) }.toList())
        if (notifications.isNotEmpty()) insert(notifications)
    }

    /** Also a deleted one (its delete waits to be sent). */
    @Query("SELECT * FROM notifications WHERE id = :id")
    suspend fun get(id: Int): NotificationEntity?

    @Query("SELECT MAX(id) FROM notifications")
    suspend fun maxId(): Int?

    @Query("UPDATE notifications SET isSeen = :seen, syncState = :state WHERE id = :id")
    suspend fun setSeen(id: Int, seen: Boolean, state: Int)

    @Query("UPDATE notifications SET deleted = 1, syncState = :state WHERE id = :id")
    suspend fun markDeleted(id: Int, state: Int)

    @Query("UPDATE notifications SET isSeen = 1 WHERE id <= :upToId")
    suspend fun markAllSeen(upToId: Int)

    @Query("UPDATE notifications SET deleted = 1 WHERE isSeen = 1 AND id <= :upToId")
    suspend fun deleteSeen(upToId: Int)

    @Query("UPDATE notifications SET syncState = :state WHERE id = :id")
    suspend fun setSyncState(id: Int, state: Int)

    @Query("DELETE FROM notifications WHERE id = :id")
    suspend fun remove(id: Int)
}

/** A snapshot's size and age, for the cache budget. */
data class SnapshotUsage(val key: String, val sizeBytes: Long, val lastUsedAt: Long)

/** The other users' content a snapshot shows. */
data class SnapshotContentRow(val key: String, val reviewIds: String, val collectionIds: String)

/** Blocking on purpose: used from OkHttp's threads by the snapshot interceptor. */
@Dao
interface SnapshotDao {
    @Query("SELECT * FROM snapshots WHERE `key` = :key")
    fun get(key: String): SnapshotEntity?

    @Upsert
    fun put(snapshot: SnapshotEntity)

    @Query("UPDATE snapshots SET lastUsedAt = :now WHERE `key` = :key")
    fun touch(key: String, now: Long)

    @Query("SELECT COALESCE(SUM(sizeBytes), 0) FROM snapshots")
    fun totalSize(): Long

    @Query("SELECT `key`, sizeBytes, lastUsedAt FROM snapshots")
    fun usage(): List<SnapshotUsage>

    @Query("SELECT `key`, reviewIds, collectionIds FROM snapshots WHERE reviewIds != '' OR collectionIds != ''")
    suspend fun withContent(): List<SnapshotContentRow>

    @Query("DELETE FROM snapshots WHERE `key` IN (:keys)")
    fun delete(keys: List<String>)

    @Query("DELETE FROM snapshots")
    suspend fun clear()
}

@Dao
interface ShadowDao {
    @Upsert
    suspend fun upsert(shadows: List<ServerShadowEntity>)

    @Query("SELECT * FROM server_shadow WHERE entityType = :entityType AND entityId = :entityId")
    suspend fun get(entityType: Int, entityId: Int): ServerShadowEntity?

    @Query("SELECT * FROM server_shadow")
    suspend fun all(): List<ServerShadowEntity>

    @Query("DELETE FROM server_shadow WHERE entityType = :entityType AND entityId = :entityId")
    suspend fun delete(entityType: Int, entityId: Int)
}
