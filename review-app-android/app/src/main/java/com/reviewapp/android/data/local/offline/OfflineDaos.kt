package com.reviewapp.android.data.local.offline

import androidx.room.Dao
import androidx.room.Embedded
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow

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

@Dao
interface OutboxDao {
    /** Changes the backend hasn't accepted yet (waiting, being sent, or in Sync issues). */
    @Query("SELECT COUNT(*) FROM outbox")
    fun observeCount(): Flow<Int>

    @Query("SELECT COUNT(*) FROM outbox")
    suspend fun count(): Int

    /** The local ids of entities of [entityType] with a pending change. */
    @Query("SELECT DISTINCT entityId FROM outbox WHERE entityType = :entityType AND entityId IS NOT NULL")
    suspend fun pendingIds(entityType: Int): List<Int>

    /** Pending changes of [entityType] that aren't about one entity (mark all seen, delete all seen). */
    @Query("SELECT COUNT(*) FROM outbox WHERE entityType = :entityType AND entityId IS NULL")
    suspend fun bulkCount(entityType: Int): Int
}

/** A review with the title and poster of its media. */
data class LocalReview(
    @Embedded val review: ReviewEntity,
    val title: String?,
    val posterUrl: String?,
)

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
}

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

    /** Replaces the follow rows, keeping the ones of users with a pending change. */
    @Transaction
    suspend fun replace(follows: List<FollowEntity>, keepUserIds: Set<Int>, users: List<KnownUserEntity>) {
        // NOT IN () would match nothing, so an id that is never a user stands in for an empty set
        deleteAllExcept(keepUserIds.ifEmpty { setOf(0) }.toList())
        upsertUsers(users)
        if (follows.isNotEmpty()) insert(follows)
    }
}

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
}
