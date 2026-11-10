package com.reviewapp.android.ui.offline

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.ConnectivityMonitor
import com.reviewapp.android.data.sync.DescribeContext
import com.reviewapp.android.data.sync.DiscardResult
import com.reviewapp.android.data.sync.OfflineGateway
import com.reviewapp.android.data.sync.OfflineWriter
import com.reviewapp.android.data.sync.OpJson
import com.reviewapp.android.data.sync.OutboxDescriber
import com.reviewapp.android.data.sync.ServerShadow
import com.reviewapp.android.data.sync.SyncEngine
import com.reviewapp.android.di.appContainer
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** One queued change as the list shows it. */
data class PendingChangeRow(
    /** The change's position (primary key) in the outbox. */
    val seq: Long,
    /** The change in plain language (see [OutboxDescriber]). */
    val description: String,
    /** Its state: waiting, sending, retrying later, or why it needs the user. */
    val status: String,
    /** The change is in Sync issues (a conflict or a rejection). */
    val needsAttention: Boolean,
    /** Not while it is being sent (its answer may be on the way). */
    val canDiscard: Boolean,
)

/** The waiting changes screen: the outbox as rows and the connectivity that decides whether syncing is offered. */
data class PendingChangesUiState(
    val rows: List<PendingChangeRow> = emptyList(),
    val isLoaded: Boolean = false,
    val isOnline: Boolean = true,
    val isSyncing: Boolean = false,
    /** The row whose Discard is being confirmed. */
    val confirming: PendingChangeRow? = null,
)

/** Observes the open offline copy's outbox, so the list follows every change and sync run, and discards changes. */
@OptIn(ExperimentalCoroutinesApi::class)
class PendingChangesViewModel(
    private val gateway: OfflineGateway,
    private val writer: OfflineWriter,
    private val syncEngine: SyncEngine,
    connectivity: ConnectivityMonitor,
) : ViewModel() {

    private val confirming = MutableStateFlow<PendingChangeRow?>(null)

    private val _messages = Channel<String>(Channel.BUFFERED)

    /** The outcome of a discard, shown once on the snackbar. */
    val messages = _messages.receiveAsFlow()

    private val rows = gateway.status
        .map { gateway.database() }
        .distinctUntilChanged()
        .flatMapLatest { db ->
            if (db == null) return@flatMapLatest flowOf(emptyList())
            db.outbox().observeAll()
                .map { queue ->
                    val context = describeContextOf(db)
                    val now = System.currentTimeMillis()
                    queue.map { op ->
                        PendingChangeRow(
                            seq = op.seq,
                            description = OutboxDescriber.describe(op, context),
                            status = OutboxDescriber.status(op, now),
                            needsAttention = op.state == OutboxState.CONFLICT || op.state == OutboxState.REJECTED,
                            canDiscard = op.state != OutboxState.IN_FLIGHT,
                        )
                    }
                }
                .catch { emit(emptyList()) }
        }

    /** The screen's state. */
    val state: StateFlow<PendingChangesUiState> = combine(
        rows,
        connectivity.connectivity,
        syncEngine.running,
        confirming,
    ) { rows, connectivity, running, confirming ->
        PendingChangesUiState(rows, isLoaded = true, isOnline = connectivity == Connectivity.Online, isSyncing = running, confirming = confirming)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), PendingChangesUiState())

    /** Asks for confirmation to discard [row]; `null` cancels. */
    fun askDiscard(row: PendingChangeRow?) = confirming.update { row }

    /** Discards the confirmed change with the later ones of the same thing and reports what happened. */
    fun discard() {
        val row = confirming.value ?: return
        confirming.value = null
        viewModelScope.launch {
            _messages.send(
                when (writer.discard(row.seq)) {
                    DiscardResult.Discarded -> "Change discarded."
                    DiscardResult.Sending -> "It's being sent right now. Try again in a moment."
                    DiscardResult.NotFound -> "It was already synced."
                },
            )
        }
    }

    /** Asks the sync engine to run now. */
    fun syncNow() = syncEngine.requestSync()

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                PendingChangesViewModel(c.offlineGateway, c.offlineWriter, c.syncEngine, c.connectivityMonitor)
            }
        }
    }
}

/**
 * Names for the describer: titles, collection names and usernames from the offline copy and, for things deleted
 * here, from the backend's version kept in the shadows (the local row is already gone then).
 */
suspend fun describeContextOf(db: OfflineDatabase): DescribeContext {
    val media = db.media().all().associate { "${it.mediaType}:${it.externalApiId}" to it.title.orEmpty() }
    val reviewTitles = HashMap<Int, String>()
    db.reviews().all().forEach { reviewTitles[it.review.id] = it.title.orEmpty() }
    val collectionNames = HashMap<Int, String>()
    db.collections().all().forEach { collectionNames[it.collection.id] = it.collection.name }
    // Deleted here: their names are only in the backend's version
    db.shadows().all().filter { it.json != ServerShadow.DELETED }.forEach { shadow ->
        runCatching {
            when (shadow.entityType) {
                EntityType.REVIEW -> reviewTitles.getOrPut(shadow.entityId) {
                    OpJson.decodeFromString(SyncReviewDto.serializer(), shadow.json).media.title.orEmpty()
                }
                EntityType.COLLECTION -> collectionNames.getOrPut(shadow.entityId) {
                    OpJson.decodeFromString(SyncCollectionDto.serializer(), shadow.json).name
                }
            }
        }
    }
    val usernames = db.follows().knownUsers().associate { it.userId to it.username }
    return DescribeContext(media.filterValues { it.isNotBlank() }, reviewTitles.filterValues { it.isNotBlank() }, collectionNames, usernames)
}
