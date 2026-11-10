package com.reviewapp.android.data.sync

import android.util.Log
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.offline.EntityType
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

    /** Whether [username] is the logged-in user (case-insensitive, like the API). */
    fun isMe(username: String): Boolean = sessionManager.currentUser?.username.equals(username, ignoreCase = true)

    /**
     * [remote] gets whether it may be answered from a snapshot. Own content refuses snapshots unless
     * [snapshotFirst] (pages whose snapshot is fuller than the tables, e.g. the profile with its counts), and falls
     * back to [local] when there is no answer; null from [local] keeps the network error.
     */
    suspend fun <T> networkFirst(
        own: Boolean,
        snapshotFirst: Boolean = false,
        /** While changes of these entity types wait to be sent, the copy is the truth: no request at all. */
        pendingTypes: List<Int> = emptyList(),
        remote: suspend (useSnapshot: Boolean) -> ApiResult<T>,
        local: suspend LocalReads.() -> T?,
    ): ApiResult<T> {
        val reads = if (own) reads() else null
        if (reads != null && pendingTypes.any { reads.hasPending(it) }) {
            try {
                reads.local()?.let { return ApiResult.Success(it) }
            } catch (e: Exception) {
                Log.w(TAG, "Offline read failed", e)
            }
        }
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

    /** True when [id] is one of the user's own reviews in the offline copy (a temporary id too). */
    suspend fun ownsReview(id: Int): Boolean =
        id < 0 || safely { database()?.reviews()?.byId(resolve(EntityType.REVIEW, id)) != null } ?: false

    /** True when [id] is one of the user's own collections in the offline copy (a temporary id too). */
    suspend fun ownsCollection(id: Int): Boolean =
        id < 0 || safely { database()?.collections()?.byId(resolve(EntityType.COLLECTION, id)) != null } ?: false

    /** The real id of something created offline once the backend has it (else [id] itself). */
    suspend fun resolve(entityType: Int, id: Int): Int {
        if (id >= 0) return id
        return safely { database()?.idMap()?.realId(entityType, id) } ?: id
    }

    /** Changes of [entityType] wait to be sent. */
    suspend fun hasPending(entityType: Int): Boolean = safely { (database()?.outbox()?.countOf(entityType) ?: 0) > 0 } ?: false

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

    /** The unsaved form text under [key], kept when the app was killed; `null` without an offline copy. */
    suspend fun loadDraft(key: String): String? = safely { database()?.drafts()?.get(key)?.json }

    /** Keeps the form text under [key] (debounced by the caller). */
    suspend fun saveDraft(key: String, json: String) {
        safely { database()?.drafts()?.put(com.reviewapp.android.data.local.offline.DraftEntity(key, json, clock())) }
    }

    /** Drops the draft once the form was saved or left on purpose. */
    suspend fun deleteDraft(key: String) {
        safely { database()?.drafts()?.delete(key) }
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
    /** Device time (epoch millis) of the last finished sync; shown as "Updated ...". */
    suspend fun lastSyncedAt(): Long? = db.syncMeta().get(SyncMetaKeys.LAST_SYNCED_AT)?.toLongOrNull()

    /** Whether changes of [entityType] wait to be sent. */
    suspend fun hasPending(entityType: Int): Boolean = db.outbox().countOf(entityType) > 0

    private suspend fun resolve(entityType: Int, id: Int): Int =
        if (id < 0) db.idMap().realId(entityType, id) ?: id else id

    /** The user's own reviews with the API's filters, sorts and paging. */
    suspend fun reviews(filter: ReviewFilter, page: Int): Page<ProfileReview> =
        LocalReviewQuery.page(db.reviews().all(), filter, page)

    /** One of the user's own reviews with its author, or `null`. */
    suspend fun reviewDetail(id: Int): ReviewDetail? {
        val review = db.reviews().byId(resolve(EntityType.REVIEW, id)) ?: return null
        val profile = db.profile().get(userId)
        return ReviewDetail(review.toProfileReview(), profile?.username ?: username, profile?.profilePictureUrl ?: profilePictureUrl)
    }

    /** The user's review of a media as the review form edits it; null = not reviewed. */
    suspend fun ownReview(mediaType: MediaType, externalApiId: String): ReviewDraft? =
        db.reviews().byMedia(mediaType.id, externalApiId)?.let {
            ReviewDraft(it.score, it.reviewText.orEmpty(), it.pros.orEmpty(), it.cons.orEmpty(), Visibility.fromId(it.visibility))
        }

    /** The user's collections sorted like the API does. */
    suspend fun collections(sortBy: String): List<MediaCollection> =
        LocalCollectionQuery.sorted(db.collections().all(), sortBy)

    /** One of the user's collections with its items, or `null`. */
    suspend fun collectionDetails(id: Int): CollectionDetails? {
        val realId = resolve(EntityType.COLLECTION, id)
        val collection = db.collections().byId(realId) ?: return null
        val items = db.collections().items(realId).mapIndexed { index, local ->
            CollectionItem(
                // Media added offline have no backend id yet; the screen keys items by it, so a unique stand-in
                dbMediaId = local.dbMediaId ?: -(index + 1),
                syncState = local.item.syncState,
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

    /** The users who follow the logged-in user, among those the device knows. */
    suspend fun followers(): List<UserSummary> =
        db.follows().followers(userId).map { UserSummary(it.userId, it.username, it.profilePictureUrl, it.isFollowed) }

    /** The users the logged-in user follows, among those the device knows. */
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

    /** A page of the notification list, with the API's keyset paging. */
    suspend fun notifications(status: NotificationStatus, sort: NotificationSort, afterId: Int?, pageSize: Int): NotificationPage =
        LocalNotificationQuery.page(db.notifications().all(), status, sort, afterId, pageSize)

    /** The unseen count the badge shows. */
    suspend fun unseenCount(): Int = db.notifications().unseenCount()

    /** Searches the media the device knows (see [OfflineSearch]). */
    suspend fun search(query: String, type: MediaType? = null): List<Media> = OfflineSearch.search(db.media().all(), query, type)
}
