package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.CollectionItemEntity
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.FollowEntity
import com.reviewapp.android.data.local.offline.KnownUserEntity
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.NotificationEntity
import com.reviewapp.android.data.local.offline.ProfileEntity
import com.reviewapp.android.data.local.offline.ServerShadowEntity
import com.reviewapp.android.data.remote.dto.SyncAccountDto
import com.reviewapp.android.data.remote.dto.SyncChangesDto
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncMediaDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.local.offline.ReviewEntity
import kotlinx.serialization.json.Json

/** Local entities with changes the backend hasn't accepted yet: a pull never overwrites them. */
data class PendingEntities(
    val reviewIds: Set<Int> = emptySet(),
    /** Collections with a pending change of their own or of their items. */
    val collectionIds: Set<Int> = emptySet(),
    /** Other users with a pending follow / unfollow / remove-follower. */
    val followUserIds: Set<Int> = emptySet(),
    val notificationIds: Set<Int> = emptySet(),
    /** A pending bio / avatar change. */
    val profile: Boolean = false,
    /** A pending "mark all seen" or "delete all seen": the notification list is local until it is sent. */
    val notificationBulk: Boolean = false,
)

/** What one page of the changes feed does to the local copy. */
data class ChangeSet(
    val reviews: List<ReviewEntity> = emptyList(),
    val deletedReviewIds: List<Int> = emptyList(),
    val collections: List<CollectionEntity> = emptyList(),
    /** The full item list of each collection in [collections] (replaces the local one). */
    val collectionItems: Map<Int, List<CollectionItemEntity>> = emptyMap(),
    val deletedCollectionIds: List<Int> = emptyList(),
    val media: List<MediaEntity> = emptyList(),
    /** The server's version of entities with pending changes ([ServerShadow.DELETED] when it was deleted). */
    val shadows: List<ServerShadowEntity> = emptyList(),
)

/** The account part of a run's first page. */
data class AccountChange(
    val profile: ProfileEntity?,
    /** The complete follow rows of the user (both directions), apart from the pending ones. */
    val follows: List<FollowEntity>,
    val keepFollowsOf: Set<Int>,
    val knownUsers: List<KnownUserEntity>,
    /** Replace the notification list with these, apart from the pending ones (null = keep the list). */
    val notifications: List<NotificationEntity>?,
    val keepNotificationIds: Set<Int>,
    val unseenCount: Int,
)

object ServerShadow {
    /** The json of a shadow whose entity was deleted on the backend. */
    const val DELETED = "null"
}

/**
 * Turns pages of `GET Sync/changes` into local changes (pure, unit-tested). The rule that keeps offline edits
 * safe: an entity with a pending change is never overwritten by a pull, only its server shadow is updated, so the
 * edit is sent against the right base and a later discard can fall back to the server's version.
 */
object ChangesApplier {

    private val json = Json { encodeDefaults = true }

    fun plan(page: SyncChangesDto, pending: PendingEntities, nowMillis: Long): ChangeSet {
        val media = (page.reviews.map { it.media } + page.collections.flatMap { c -> c.items.map { it.media } })
            .distinctBy { it.mediaType to it.externalApiId }
            .map { it.toEntity(nowMillis) }

        val (pendingReviews, reviews) = page.reviews.partition { it.id in pending.reviewIds }
        val (pendingCollections, collections) = page.collections.partition { it.id in pending.collectionIds }
        val (shadowedReviewDeletes, deletedReviews) = page.deletedReviewIds.partition { it in pending.reviewIds }
        val (shadowedCollectionDeletes, deletedCollections) = page.deletedCollectionIds.partition { it in pending.collectionIds }

        val shadows = pendingReviews.map { ServerShadowEntity(EntityType.REVIEW, it.id, json.encodeToString(SyncReviewDto.serializer(), it), it.version) } +
            pendingCollections.map { ServerShadowEntity(EntityType.COLLECTION, it.id, json.encodeToString(SyncCollectionDto.serializer(), it), it.version) } +
            shadowedReviewDeletes.map { ServerShadowEntity(EntityType.REVIEW, it, ServerShadow.DELETED, null) } +
            shadowedCollectionDeletes.map { ServerShadowEntity(EntityType.COLLECTION, it, ServerShadow.DELETED, null) }

        return ChangeSet(
            reviews = reviews.map { it.toEntity() },
            deletedReviewIds = deletedReviews,
            collections = collections.map { it.toEntity() },
            collectionItems = collections.associate { c -> c.id to c.items.map { it.toEntity(c.id) } },
            deletedCollectionIds = deletedCollections,
            media = media,
            shadows = shadows,
        )
    }

    /**
     * After a full resync: local rows the run didn't send were deleted on the backend while the device couldn't
     * know (their deletion records are gone). Rows only the device has (negative ids) and pending ones stay.
     */
    fun staleIds(localIds: Collection<Int>, seenIds: Set<Int>, pendingIds: Set<Int>): List<Int> =
        localIds.filter { it > 0 && it !in seenIds && it !in pendingIds }

    fun account(account: SyncAccountDto, pending: PendingEntities, nowMillis: Long): AccountChange {
        val me = account.userId
        val follows = account.following.filter { it.id !in pending.followUserIds }.map { FollowEntity(me, it.id) } +
            account.followers.filter { it.id !in pending.followUserIds }.map { FollowEntity(it.id, me) }
        val users = (account.following + account.followers).distinctBy { it.id }
            .map { KnownUserEntity(it.id, it.username, it.profilePictureUrl?.takeIf(String::isNotBlank), lastUsedAt = nowMillis) }
        val notifications = if (pending.notificationBulk) null else account.notifications
            .filter { it.id !in pending.notificationIds }
            .map {
                NotificationEntity(
                    id = it.id, type = it.type, title = it.title, message = it.message, isSeen = it.isSeen,
                    createdAt = it.createdAt, actorUsername = it.actorUsername, actorProfilePictureUrl = it.actorProfilePictureUrl,
                    reviewId = it.reviewId, replyId = it.replyId, groupCode = it.groupCode,
                )
            }
        return AccountChange(
            profile = if (pending.profile) null else ProfileEntity(me, account.username, account.bio, account.profilePictureUrl?.takeIf(String::isNotBlank)),
            follows = follows,
            keepFollowsOf = pending.followUserIds,
            knownUsers = users,
            notifications = notifications,
            keepNotificationIds = pending.notificationIds,
            unseenCount = account.unseenNotificationCount,
        )
    }

    fun SyncReviewDto.toEntity() = ReviewEntity(
        id = id,
        mediaType = media.mediaType,
        externalApiId = media.externalApiId,
        score = score,
        reviewText = reviewText,
        pros = pros,
        cons = cons,
        visibility = visibility,
        createdAt = createdAt,
        updatedAt = updatedAt,
        version = version,
    )

    fun SyncCollectionDto.toEntity() = CollectionEntity(
        id = id,
        name = name,
        visibility = visibility,
        isDefault = isDefault,
        createdAt = createdAt,
        updatedAt = updatedAt,
        version = version,
    )

    private fun com.reviewapp.android.data.remote.dto.SyncCollectionItemDto.toEntity(collectionId: Int) =
        CollectionItemEntity(collectionId, media.mediaType, media.externalApiId, orderIndex, addedAt)

    fun SyncMediaDto.toEntity(nowMillis: Long) = MediaEntity(
        mediaType = mediaType,
        externalApiId = externalApiId,
        dbMediaId = dbMediaId.takeIf { it > 0 },
        title = title,
        posterUrl = posterUrl?.takeIf(String::isNotBlank),
        releaseDate = releaseDate,
        creator = creator,
        lastUsedAt = nowMillis,
    )
}
