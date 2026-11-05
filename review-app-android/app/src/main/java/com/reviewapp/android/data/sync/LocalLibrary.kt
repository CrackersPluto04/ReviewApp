package com.reviewapp.android.data.sync

import android.util.Log
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.SyncMetaKeys
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.data.sync.LocalCollectionQuery.toMediaCollection
import com.reviewapp.android.data.sync.LocalReviewQuery.toProfileReview
import com.reviewapp.android.domain.model.CollectionDetails
import com.reviewapp.android.domain.model.CollectionItem
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.UserSummary
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.domain.model.mediaKey

/**
 * The repositories' way into the logged-in user's offline copy. Every read is network first: the API answers
 * whenever it can, and only a request without an answer ([ApiResult.Error.isNetwork]) falls back to the device.
 * The user's own content is read from the synced tables (complete, never a stale snapshot); anything else comes
 * from the snapshots of [SnapshotInterceptor]. Without offline mode ([OfflineGateway.database] null) nothing
 * changes: the API's answer is returned as it is.
 */
class LocalLibrary(
    private val gateway: OfflineGateway,
    private val sessionManager: SessionManager,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    /** The logged-in user's offline database, while offline mode uses one. */
    fun database(): OfflineDatabase? = gateway.database()

    fun isMe(username: String): Boolean = sessionManager.currentUser?.username.equals(username, ignoreCase = true)

    /**
     * [remote] gets whether it may be answered from a snapshot. Own content refuses snapshots unless
     * [snapshotFirst] (pages whose snapshot is fuller than the tables, e.g. the profile with its counts), and falls
     * back to [local] when there is no answer; null from [local] keeps the network error.
     */
    suspend fun <T> networkFirst(
        own: Boolean,
        snapshotFirst: Boolean = false,
        remote: suspend (useSnapshot: Boolean) -> ApiResult<T>,
        local: suspend LocalReads.() -> T?,
    ): ApiResult<T> {
        val reads = if (own) reads() else null
        val result = remote(reads == null || snapshotFirst)
        if (reads == null || result !is ApiResult.Error || !result.isNetwork) return result
        return try {
            reads.local()?.let { ApiResult.Success(it, cachedAt = reads.lastSyncedAt() ?: clock()) } ?: result
        } catch (e: Exception) {
            // Closed under us (logged out meanwhile)
            Log.w(TAG, "Offline read failed", e)
            result
        }
    }

    /** The reads of the current copy, null without one or before its first download finished. */
    suspend fun reads(): LocalReads? {
        val db = database() ?: return null
        val me = sessionManager.currentUser ?: return null
        return try {
            if (db.syncMeta().get(SyncMetaKeys.PREPARED) != "true") null else LocalReads(db, me.id, me.username, me.profilePictureUrl, me.isPremium)
        } catch (_: Exception) {
            null
        }
    }

    /** True when [id] is one of the user's own reviews in the offline copy. */
    suspend fun ownsReview(id: Int): Boolean = safely { database()?.reviews()?.byId(id) != null } ?: false

    suspend fun ownsCollection(id: Int): Boolean = safely { database()?.collections()?.byId(id) != null } ?: false

    /** Keeps an opened media for the offline search (the most recently used ones stay). */
    suspend fun rememberMedia(media: Media) {
        val db = database() ?: return
        safely {
            val existing = db.media().get(media.mediaType.id, media.externalApiId)
            db.media().upsert(
                listOf(
                    MediaEntity(
                        mediaType = media.mediaType.id,
                        externalApiId = media.externalApiId,
                        dbMediaId = existing?.dbMediaId,
                        title = media.title,
                        posterUrl = media.posterUrl,
                        releaseDate = media.releaseDate,
                        creator = media.creator.ifBlank { existing?.creator },
                        lastUsedAt = clock(),
                    ),
                ),
            )
        }
    }

    /**
     * "Clear cached data": the kept answers and the opened media go; the user's own content, its posters and the
     * changes waiting to be sent stay.
     */
    suspend fun clearCache() {
        val db = database() ?: return
        safely {
            db.snapshots().clear()
            db.media().deleteUnreferenced()
        }
    }

    /** The reply counts of the user's reviews as the API just showed them (the feed doesn't carry them). */
    suspend fun rememberReplyCounts(reviews: List<ProfileReview>) {
        val db = database() ?: return
        safely { reviews.filter { it.isOwner }.forEach { db.reviews().setReplyCount(it.id, it.replyCount) } }
    }

    private suspend fun <T> safely(block: suspend () -> T): T? = try {
        block()
    } catch (e: Exception) {
        Log.w(TAG, "Offline copy not available", e)
        null
    }

    private companion object {
        const val TAG = "LocalLibrary"
    }
}

/** Reads of one user's offline copy, answering in the API's domain models. */
class LocalReads(
    private val db: OfflineDatabase,
    private val userId: Int,
    private val username: String,
    private val profilePictureUrl: String?,
    private val isPremium: Boolean,
) {
    suspend fun lastSyncedAt(): Long? = db.syncMeta().get(SyncMetaKeys.LAST_SYNCED_AT)?.toLongOrNull()

    suspend fun reviews(filter: ReviewFilter, page: Int): Page<ProfileReview> =
        LocalReviewQuery.page(db.reviews().all(), filter, page)

    suspend fun reviewDetail(id: Int): ReviewDetail? {
        val review = db.reviews().byId(id) ?: return null
        val profile = db.profile().get(userId)
        return ReviewDetail(review.toProfileReview(), profile?.username ?: username, profile?.profilePictureUrl ?: profilePictureUrl)
    }

    /** The user's review of a media as the review form edits it; null = not reviewed. */
    suspend fun ownReview(mediaType: MediaType, externalApiId: String): ReviewDraft? =
        db.reviews().byMedia(mediaType.id, externalApiId)?.let {
            ReviewDraft(it.score, it.reviewText.orEmpty(), it.pros.orEmpty(), it.cons.orEmpty(), Visibility.fromId(it.visibility))
        }

    suspend fun collections(sortBy: String): List<MediaCollection> =
        LocalCollectionQuery.sorted(db.collections().all(), sortBy)

    suspend fun collectionDetails(id: Int): CollectionDetails? {
        val collection = db.collections().byId(id) ?: return null
        val items = db.collections().items(id).map { local ->
            CollectionItem(
                dbMediaId = local.dbMediaId ?: 0,
                media = Media(
                    externalApiId = local.item.externalApiId,
                    mediaType = MediaType.fromId(local.item.mediaType),
                    title = local.title.orEmpty(),
                    releaseDate = local.releaseDate?.takeIf { it.isNotBlank() },
                    posterUrl = local.posterUrl,
                    overview = "",
                    creator = local.creator.orEmpty(),
                ),
            )
        }
        return CollectionDetails(collection.toMediaCollection(), items)
    }

    /** Media key -> the ids of the user's collections containing it. */
    suspend fun memberships(): Map<String, Set<Int>> =
        db.collections().allItems()
            .groupBy({ mediaKey(MediaType.fromId(it.mediaType), it.externalApiId) }, { it.collectionId })
            .mapValues { it.value.toSet() }

    suspend fun followers(): List<UserSummary> =
        db.follows().followers(userId).map { UserSummary(it.userId, it.username, it.profilePictureUrl, it.isFollowed) }

    suspend fun following(): List<UserSummary> =
        db.follows().following(userId).map { UserSummary(it.userId, it.username, it.profilePictureUrl, it.isFollowed) }

    /** The profile header from the tables, when no snapshot of the page exists (the join date isn't known). */
    suspend fun profile(): UserProfile {
        val profile = db.profile().get(userId)
        return UserProfile(
            id = userId,
            username = profile?.username ?: username,
            bio = profile?.bio.orEmpty(),
            profilePictureUrl = profile?.profilePictureUrl ?: profilePictureUrl,
            joinedAt = "",
            followersCount = db.follows().followersCount(userId),
            followingCount = db.follows().followingCount(userId),
            isFollowed = false,
            isPremium = isPremium,
        )
    }

    suspend fun notifications(status: NotificationStatus, sort: NotificationSort, afterId: Int?, pageSize: Int): NotificationPage =
        LocalNotificationQuery.page(db.notifications().all(), status, sort, afterId, pageSize)

    suspend fun unseenCount(): Int = db.notifications().unseenCount()

    suspend fun search(query: String, type: MediaType? = null): List<Media> = OfflineSearch.search(db.media().all(), query, type)
}
