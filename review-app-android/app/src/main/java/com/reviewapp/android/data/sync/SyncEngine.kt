package com.reviewapp.android.data.sync

import android.util.Log
import androidx.room.withTransaction
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.SyncMetaKeys
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.withTimeoutOrNull
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
    /** The queue was sent and the changes feed pulled. */
    data object Done : SyncOutcome


    /** Nothing to do: no offline copy, offline, or a mode that doesn't pull. */
    data object Skipped : SyncOutcome
    /** The run stopped; [isNetwork] means the backend could not be reached. */
    data class Failed(val message: String, val isNetwork: Boolean) : SyncOutcome
}

/** Where an awaited op is ([SyncEngine.awaitOp]). */
sealed interface OpWait {
    /** The op left the queue: the backend accepted it. */
    data object Applied : OpWait


    /** Still queued (offline, or no answer within the wait). */
    data object Queued : OpWait
    /** The backend found a conflict or refused it; [reason] is its reason code. */
    data class Issue(val reason: String?) : OpWait
}

/**
 * Keeps the offline copy and the backend in step: one run at a time (a [Mutex]), triggers while one runs fold into
 * one more run (a conflated channel). A run first sends the queued changes ([PushRunner]), then pulls the changes
 * feed into the tables page by page (each page in one transaction, so an interruption leaves a consistent copy;
 * pulling after pushing means a refresh never lands on top of a change still waiting), then drops cached content
 * its owners have hidden since, and keeps the cache within its budget.
 */
class SyncEngine(
    private val syncApi: SyncApi,
    private val gateway: OfflineGateway,
    private val connectivity: ConnectivityMonitor,
    private val snapshots: SnapshotInterceptor,
    private val pushRunner: PushRunner,
    private val scope: CoroutineScope,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val mutex = Mutex()
    private val triggers = Channel<Unit>(Channel.CONFLATED)

    private val _running = MutableStateFlow(false)

    /** Whether a run is in progress (the connectivity strip shows "Syncing"). */
    val running: StateFlow<Boolean> = _running.asStateFlow()

    /** Set while the backend asked to slow down (429): the sync continues by itself at that time. */
    private val _pausedUntil = MutableStateFlow<Long?>(null)
    val pausedUntil: StateFlow<Long?> = _pausedUntil.asStateFlow()

    private val _lastOutcome = MutableStateFlow<SyncOutcome?>(null)

    /** How the last run ended; `null` before the first. */
    val lastOutcome: StateFlow<SyncOutcome?> = _lastOutcome.asStateFlow()

    /** After a push that applied changes or put some in Sync issues (lists reload: real ids, markers, hearts). */
    private val _pushed = MutableSharedFlow<PushOutcome.Done>(extraBufferCapacity = 4)
    /** Emitted after a push that applied changes or created issues. */
    val pushed: SharedFlow<PushOutcome.Done> = _pushed.asSharedFlow()

    /** After a run in the app that sent changes which had waited: the root shows the sync summary. */
    private val _summaries = MutableSharedFlow<PushOutcome.Done>(extraBufferCapacity = 4)
    /** Emitted after a run that sent changes which had waited a while. */
    val summaries: SharedFlow<PushOutcome.Done> = _summaries.asSharedFlow()

    private var lastVisibilityCheck = 0L
    private var recoveredDb: OfflineDatabase? = null
    private var wakeUp: Job? = null

    /**
     * Starts the triggers: the user getting an offline copy, the connection coming back, [changes] (the user's own
     * online writes, which change the content on the backend, debounced) and a periodic run every 10 minutes so
     * changes from the web or another device arrive by themselves.
     */
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

    /** Asks for a run; several requests while one runs become one more run. */
    fun requestSync() {
        triggers.trySend(Unit)
    }

    /** One run now (the settings' "Sync now", the library download); waits for a running one first. */
    suspend fun syncNow(): SyncOutcome = runOnce()

    /** Only sends the queue (the background job: no pull, no banners). */
    suspend fun pushOnly(): PushOutcome? = mutex.withLock {
        val db = gateway.database() ?: return@withLock null
        try {
            recoverOnce(db)
            pushRunner.push(db).also { if (it is PushOutcome.Done && (it.applied > 0 || it.issues > 0)) _pushed.tryEmit(it) }
        } catch (e: Exception) {
            Log.w(TAG, "Push failed", e)
            PushOutcome.Failed(e.message ?: "Sync failed")
        }
    }

    /**
     * Waits a moment for one op's answer while online (so a form can show "name taken" like it did before offline
     * mode); offline, or without an answer in time, the op just stays queued.
     */
    suspend fun awaitOp(opId: String, timeoutMillis: Long = AWAIT_MILLIS): OpWait {
        if (connectivity.connectivity.value != Connectivity.Online) return OpWait.Queued
        val db = gateway.database() ?: return OpWait.Queued
        requestSync()
        return withTimeoutOrNull(timeoutMillis) { pollOp(db, opId) } ?: OpWait.Queued
    }

    private suspend fun pollOp(db: OfflineDatabase, opId: String): OpWait {
        while (true) {
            val op = try {
                db.outbox().all().firstOrNull { it.opId == opId }
            } catch (_: Exception) {
                return OpWait.Queued
            }
            when {
                op == null -> return OpWait.Applied
                op.state == OutboxState.CONFLICT || op.state == OutboxState.REJECTED -> return OpWait.Issue(op.lastReason)
            }
            delay(AWAIT_POLL_MILLIS)
        }
    }

    private suspend fun runOnce(): SyncOutcome = mutex.withLock {
        val mode = gateway.status.value.mode
        val db = gateway.database()
        if (db == null || connectivity.connectivity.value != Connectivity.Online) return@withLock SyncOutcome.Skipped
        _running.value = true
        val outcome = try {
            recoverOnce(db)
            when (val pushed = pushRunner.push(db)) {
                PushOutcome.Offline -> SyncOutcome.Failed("You're offline.", isNetwork = true)
                PushOutcome.NeedsLogin -> SyncOutcome.Failed("Log in again to sync your changes.", isNetwork = false)
                else -> {
                    if (pushed is PushOutcome.Done && (pushed.applied > 0 || pushed.issues > 0)) _pushed.tryEmit(pushed)
                    if (pushed is PushOutcome.Done && (pushed.waited > 0 || pushed.waitedIssues > 0)) _summaries.tryEmit(pushed)
                    _pausedUntil.value = (pushed as? PushOutcome.Paused)?.untilMillis
                    // Draining only sends what waits (the copy goes away), a pull would be wasted
                    if (mode is OfflineMode.Draining) SyncOutcome.Done
                    else pull(db).also { if (it == SyncOutcome.Done) afterPull(db) }
                }
            }
        } catch (e: Exception) {
            // The copy was closed (logout, switched off) or the device ran out of space
            Log.w(TAG, "Sync run failed", e)
            SyncOutcome.Failed(e.message ?: "Sync failed", isNetwork = false)
        } finally {
            _running.value = false
        }
        scheduleWakeUp(db)
        _lastOutcome.value = outcome
        outcome
    }

    private suspend fun recoverOnce(db: OfflineDatabase) {
        if (recoveredDb === db) return
        pushRunner.recover(db)
        recoveredDb = db
    }

    /** Runs again when the earliest waiting op may be retried (or the 429 pause ends). */
    private suspend fun scheduleWakeUp(db: OfflineDatabase) {
        val next = try {
            listOfNotNull(db.outbox().nextAttemptAt(), _pausedUntil.value).filter { it > clock() }.minOrNull()
        } catch (_: Exception) {
            null
        } ?: return
        wakeUp?.cancel()
        wakeUp = scope.launch {
            delay(next - clock())
            _pausedUntil.value = null
            requestSync()
        }
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
            followUserIds = (outbox.pendingIds(EntityType.FOLLOW) + outbox.pendingIds(EntityType.FOLLOWER)).toSet(),
            notificationIds = outbox.pendingIds(EntityType.NOTIFICATION).toSet(),
            profile = outbox.pendingIds(EntityType.PROFILE).isNotEmpty() || outbox.bulkCount(EntityType.PROFILE) > 0,
            notificationBulk = outbox.bulkCount(EntityType.NOTIFICATION) > 0,
        )
    }

    private suspend fun apply(db: OfflineDatabase, set: ChangeSet) {
        db.media().upsert(set.media)
        // Deletions first: a title reviewed again after its review was deleted arrives with a new id
        if (set.deletedReviewIds.isNotEmpty()) db.reviews().delete(set.deletedReviewIds)
        if (set.reviews.isNotEmpty()) {
            // Reply counts aren't in the feed: keep the ones last seen online
            val counts = db.reviews().replyCounts(set.reviews.map { it.id }).associate { it.id to it.replyCount }
            val pendingIds = db.outbox().pendingIds(EntityType.REVIEW).toSet()
            val reviews = set.reviews.filter { review ->
                // One review per title: another row of the same title would make the upsert do nothing. A stale
                // one goes; one with waiting changes (written here) stays, the push reports the conflict.
                val other = db.reviews().byMedia(review.mediaType, review.externalApiId)?.takeIf { it.id != review.id }
                when {
                    other == null -> true
                    other.id in pendingIds -> false
                    else -> {
                        db.reviews().delete(listOf(other.id))
                        true
                    }
                }
            }
            db.reviews().upsert(reviews.map { it.copy(replyCount = counts[it.id] ?: 0) })
        }
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
        const val AWAIT_MILLIS = 5_000L
        const val AWAIT_POLL_MILLIS = 150L
        const val PERIODIC_MILLIS = 10 * 60_000L
        const val VISIBILITY_EVERY_MILLIS = 30 * 60_000L
        /** Opened media kept for the offline search beyond the user's own content. */
        const val KEEP_OPENED_MEDIA = 2_000
    }
}
