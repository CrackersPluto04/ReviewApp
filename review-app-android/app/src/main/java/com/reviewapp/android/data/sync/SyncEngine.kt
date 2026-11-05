package com.reviewapp.android.data.sync

import android.util.Log
import androidx.room.withTransaction
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.SyncMetaKeys
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.SyncApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.SyncChangesDto
import com.reviewapp.android.data.remote.dto.SyncVisibleRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.merge
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** What the last sync run did. */
sealed interface SyncOutcome {
    data object Done : SyncOutcome
    /** Nothing to do: no offline copy, offline, or a mode that doesn't pull. */
    data object Skipped : SyncOutcome
    data class Failed(val message: String, val isNetwork: Boolean) : SyncOutcome
}

/**
 * Keeps the offline copy current: one run at a time (a [Mutex]), triggers while one runs fold into one more run
 * (a conflated channel). A run pulls the changes feed into the tables page by page (each page in one transaction,
 * so an interruption leaves a consistent copy), then drops cached content its owners have hidden since, and keeps
 * the cache within its budget. Offline editing adds the push in front of the pull.
 */
class SyncEngine(
    private val syncApi: SyncApi,
    private val gateway: OfflineGateway,
    private val connectivity: ConnectivityMonitor,
    private val snapshots: SnapshotInterceptor,
    private val scope: CoroutineScope,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val mutex = Mutex()
    private val triggers = Channel<Unit>(Channel.CONFLATED)

    private val _running = MutableStateFlow(false)
    val running: StateFlow<Boolean> = _running.asStateFlow()

    private val _lastOutcome = MutableStateFlow<SyncOutcome?>(null)
    val lastOutcome: StateFlow<SyncOutcome?> = _lastOutcome.asStateFlow()

    private var lastVisibilityCheck = 0L

    /** [changes]: the user's own online writes (they change the copy on the backend). */
    @OptIn(FlowPreview::class, ExperimentalCoroutinesApi::class)
    fun start(changes: Flow<Unit>) {
        scope.launch { for (trigger in triggers) runOnce() }
        scope.launch {
            merge(
                // A user got an offline copy (logged in, switched on, Premium started)
                gateway.status.map { it.mode.takeIf { mode -> mode.usesLocalCopy } }.distinctUntilChanged().filter { it != null }.map { },
                // Back online
                connectivity.connectivity.filter { it == Connectivity.Online }.map { },
                changes.debounce(CHANGES_DEBOUNCE_MILLIS),
            ).collect { requestSync() }
        }
        // Changes made on the web or another device arrive without the user doing anything here
        scope.launch {
            while (true) {
                delay(PERIODIC_MILLIS)
                requestSync()
            }
        }
    }

    fun requestSync() {
        triggers.trySend(Unit)
    }

    /** One run now (the settings' "Sync now", the library download); waits for a running one first. */
    suspend fun syncNow(): SyncOutcome = runOnce()

    private suspend fun runOnce(): SyncOutcome = mutex.withLock {
        val mode = gateway.status.value.mode
        val db = gateway.database()
        // Draining only sends what waits (the copy goes away), a pull would be wasted
        if (db == null || mode is OfflineMode.Draining || connectivity.connectivity.value != Connectivity.Online) {
            return@withLock SyncOutcome.Skipped
        }
        _running.value = true
        val outcome = try {
            pull(db).also { if (it == SyncOutcome.Done) afterPull(db) }
        } catch (e: Exception) {
            // The copy was closed (logout, switched off) or the device ran out of space
            Log.w(TAG, "Sync run failed", e)
            SyncOutcome.Failed(e.message ?: "Sync failed", isNetwork = false)
        } finally {
            _running.value = false
        }
        _lastOutcome.value = outcome
        outcome
    }

    private suspend fun pull(db: OfflineDatabase): SyncOutcome {
        val meta = db.syncMeta()
        var since = meta.get(SyncMetaKeys.CURSOR)?.toLongOrNull() ?: 0
        val syncedAt = meta.get(SyncMetaKeys.LAST_SYNCED_SERVER_TIME)
        var continued = false
        var fullResync = false
        val seenReviews = HashSet<Int>()
        val seenCollections = HashSet<Int>()
        var last: SyncChangesDto

        while (true) {
            val page = when (val result = apiCall(useSnapshot = false) { syncApi.changes(since, syncedAt, continued, PAGE_SIZE) }) {
                is ApiResult.Success -> result.data
                is ApiResult.Error -> return SyncOutcome.Failed(result.message, result.isNetwork)
            }
            if (!continued) fullResync = page.fullResync
            val now = clock()
            db.withTransaction {
                val pending = pendingEntities(db)
                apply(db, ChangesApplier.plan(page, pending, now))
                page.account?.let { applyAccount(db, ChangesApplier.account(it, pending, now)) }
                // A full resync only counts once it finished: an interrupted one starts over
                if (!fullResync) meta.put(SyncMetaKeys.CURSOR, page.cursor.toString())
            }
            seenReviews += page.reviews.map { it.id }
            seenCollections += page.collections.map { it.id }
            last = page
            if (!page.hasMore) break
            since = page.cursor
            continued = true
        }

        db.withTransaction {
            if (fullResync) {
                val pending = pendingEntities(db)
                db.reviews().delete(ChangesApplier.staleIds(db.reviews().ids(), seenReviews, pending.reviewIds))
                db.collections().delete(ChangesApplier.staleIds(db.collections().ids(), seenCollections, pending.collectionIds))
                meta.put(SyncMetaKeys.CURSOR, last.cursor.toString())
            }
            meta.put(SyncMetaKeys.LAST_SYNCED_SERVER_TIME, last.serverTime)
            meta.put(SyncMetaKeys.LAST_SYNCED_AT, clock().toString())
            meta.put(SyncMetaKeys.PREPARED, "true")
        }
        return SyncOutcome.Done
    }

    private suspend fun pendingEntities(db: OfflineDatabase): PendingEntities {
        val outbox = db.outbox()
        return PendingEntities(
            reviewIds = outbox.pendingIds(EntityType.REVIEW).toSet(),
            collectionIds = outbox.pendingIds(EntityType.COLLECTION).toSet(),
            followUserIds = outbox.pendingIds(EntityType.FOLLOW).toSet(),
            notificationIds = outbox.pendingIds(EntityType.NOTIFICATION).toSet(),
            profile = outbox.pendingIds(EntityType.PROFILE).isNotEmpty() || outbox.bulkCount(EntityType.PROFILE) > 0,
            notificationBulk = outbox.bulkCount(EntityType.NOTIFICATION) > 0,
        )
    }

    private suspend fun apply(db: OfflineDatabase, set: ChangeSet) {
        db.media().upsert(set.media)
        if (set.reviews.isNotEmpty()) {
            // Reply counts aren't in the feed: keep the ones last seen online
            val counts = db.reviews().replyCounts(set.reviews.map { it.id }).associate { it.id to it.replyCount }
            db.reviews().upsert(set.reviews.map { it.copy(replyCount = counts[it.id] ?: 0) })
        }
        if (set.deletedReviewIds.isNotEmpty()) db.reviews().delete(set.deletedReviewIds)
        // Upsert never deletes the row, so the items' cascade isn't triggered
        if (set.collections.isNotEmpty()) db.collections().upsert(set.collections)
        set.collectionItems.forEach { (id, items) -> db.collections().replaceItems(id, items) }
        if (set.deletedCollectionIds.isNotEmpty()) db.collections().delete(set.deletedCollectionIds)
        if (set.shadows.isNotEmpty()) db.shadows().upsert(set.shadows)
    }

    private suspend fun applyAccount(db: OfflineDatabase, change: AccountChange) {
        change.profile?.let { db.profile().upsert(it) }
        db.follows().replace(change.follows, change.keepFollowsOf, change.knownUsers)
        change.notifications?.let { db.notifications().replace(it, change.keepNotificationIds) }
        db.syncMeta().put(SyncMetaKeys.UNSEEN_COUNT, change.unseenCount.toString())
    }

    private suspend fun afterPull(db: OfflineDatabase) {
        try {
            if (clock() - lastVisibilityCheck >= VISIBILITY_EVERY_MILLIS) {
                pruneHidden(db)
                lastVisibilityCheck = clock()
            }
            db.media().pruneUnreferenced(KEEP_OPENED_MEDIA)
            snapshots.evict(db)
        } catch (e: Exception) {
            Log.w(TAG, "Cache upkeep failed", e)
        }
    }

    /** Drops snapshots that show other users' content the user can't see any more (made private, unfollowed). */
    private suspend fun pruneHidden(db: OfflineDatabase) {
        val rows = db.snapshots().withContent()
        if (rows.isEmpty()) return
        val ownReviews = db.reviews().ids().toSet()
        val ownCollections = db.collections().ids().toSet()
        val reviews = rows.flatMapTo(HashSet()) { SnapshotContent.parseIds(it.reviewIds) } - ownReviews
        val collections = rows.flatMapTo(HashSet()) { SnapshotContent.parseIds(it.collectionIds) } - ownCollections

        val hiddenReviews = HashSet<Int>()
        val hiddenCollections = HashSet<Int>()
        val reviewChunks = reviews.chunked(MAX_VISIBLE_IDS)
        val collectionChunks = collections.chunked(MAX_VISIBLE_IDS)
        for (i in 0 until maxOf(reviewChunks.size, collectionChunks.size)) {
            val askedReviews = reviewChunks.getOrElse(i) { emptyList() }
            val askedCollections = collectionChunks.getOrElse(i) { emptyList() }
            val visible = when (val result = apiCall(useSnapshot = false) { syncApi.visible(SyncVisibleRequest(askedReviews, askedCollections)) }) {
                is ApiResult.Success -> result.data
                is ApiResult.Error -> return
            }
            hiddenReviews += askedReviews - visible.reviewIds.toSet()
            hiddenCollections += askedCollections - visible.collectionIds.toSet()
        }
        val doomed = rows.filter { row ->
            SnapshotContent.parseIds(row.reviewIds).any { it in hiddenReviews } ||
                SnapshotContent.parseIds(row.collectionIds).any { it in hiddenCollections }
        }.map { it.key }
        doomed.chunked(500).forEach { db.snapshots().delete(it) }
    }

    private companion object {
        const val TAG = "SyncEngine"
        /** The feed's page size (its maximum). */
        const val PAGE_SIZE = 500
        /** The API's limit of ids per visibility check. */
        const val MAX_VISIBLE_IDS = 1000
        const val CHANGES_DEBOUNCE_MILLIS = 2_000L
        const val PERIODIC_MILLIS = 10 * 60_000L
        const val VISIBILITY_EVERY_MILLIS = 30 * 60_000L
        /** Opened media kept for the offline search beyond the user's own content. */
        const val KEEP_OPENED_MEDIA = 2_000
    }
}
