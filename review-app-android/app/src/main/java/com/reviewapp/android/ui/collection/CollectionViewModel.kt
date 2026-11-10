package com.reviewapp.android.ui.collection

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.toRoute
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.CollectionItem
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.navigation.CollectionRoute
import com.reviewapp.android.util.moved
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** What the collection screen shows: the collection and its items plus the state of its dialogs. */
data class CollectionUiState(
    val collection: MediaCollection? = null,
    val items: List<CollectionItem> = emptyList(),
    val isLoading: Boolean = true,
    val error: String? = null,
    /** The collection does not exist or is not visible to this user (a 404 looks the same for both). */
    val notFound: Boolean = false,
    val showEditDialog: Boolean = false,
    val isSaving: Boolean = false,
    val editError: String? = null,
    val askDelete: Boolean = false,
    val isDeleting: Boolean = false,
    /** The item whose removal awaits confirmation. */
    val itemToRemove: CollectionItem? = null,
) {
    val isOwner: Boolean get() = collection?.isOwner == true

    /** Favourites can't be renamed, edited or deleted. */
    val canEdit: Boolean get() = isOwner && collection?.isDefault == false
}

/** One-off results the screen reacts to. */
sealed interface CollectionEvent {
    /** A failure to show on the snackbar. */
    data class Message(val text: String) : CollectionEvent

    /** The collection was deleted, so the screen closes. */
    data object Deleted : CollectionEvent
}

/**
 * One collection's screen: loading, the drag-and-drop reordering, removing items and editing or deleting the
 * collection. Writes go through [CollectionRepository], so offline they are queued like any other change.
 */
class CollectionViewModel(
    private val collectionId: Int,
    private val collectionRepository: CollectionRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(CollectionUiState())
    val state: StateFlow<CollectionUiState> = _state.asStateFlow()

    private val _events = Channel<CollectionEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    private var loadJob: Job? = null
    // Reorder requests must reach the API in the order they were made
    private val reorderMutex = Mutex()
    private var dragStartIndex: Int? = null

    init {
        load()
        viewModelScope.launch {
            // E.g. the media was taken out of this collection from its details page; a sync may have changed its
            // id or markers. Not while an item is being dragged: a reload would take it out of the user's hand
            collectionRepository.changes
                .filter { (it.collectionId == collectionId || it.fromSync) && dragStartIndex == null }
                .collect { load(quiet = true) }
        }
    }

    /** Loads the collection; [quiet] refreshes in the background without a spinner and ignores a failure. */
    fun load(quiet: Boolean = false) {
        loadJob?.cancel()
        if (!quiet) _state.update { it.copy(isLoading = true, error = null, notFound = false) }
        loadJob = viewModelScope.launch {
            when (val result = collectionRepository.details(collectionId)) {
                is ApiResult.Success -> _state.update {
                    it.copy(collection = result.data.collection, items = result.data.items, isLoading = false, error = null)
                }
                is ApiResult.Error -> if (!quiet) {
                    _state.update { it.copy(isLoading = false, error = result.message, notFound = result.code == 404) }
                }
            }
        }
    }

    // Reordering: the list changes locally while dragging, one request is sent when the item is dropped

    /** A drag of the item with this database media id began; remembers where it started. */
    fun startDrag(dbMediaId: Int) {
        dragStartIndex = _state.value.items.indexOfFirst { it.dbMediaId == dbMediaId }
    }

    /** Moves the dragged item to the position of another one (both given by their list keys, the db media ids). */
    fun move(fromKey: Any?, toKey: Any?) = _state.update { state ->
        val from = state.items.indexOfFirst { it.dbMediaId == fromKey }
        val to = state.items.indexOfFirst { it.dbMediaId == toKey }
        if (from < 0 || to < 0) state else state.copy(items = state.items.moved(from, to))
    }

    /** The item was dropped: sends its final index when it moved, and reloads if that failed. */
    fun endDrag(dbMediaId: Int) {
        val start = dragStartIndex ?: return
        dragStartIndex = null
        val end = _state.value.items.indexOfFirst { it.dbMediaId == dbMediaId }
        if (end < 0 || end == start) return
        viewModelScope.launch {
            val order = _state.value.items.map { it.media }
            val result = reorderMutex.withLock { collectionRepository.reorder(collectionId, dbMediaId, end, order) }
            if (result is ApiResult.Error) {
                _events.send(CollectionEvent.Message(result.message))
                load(quiet = true)
            }
        }
    }

    /** Asks for confirmation to remove [item]; `null` cancels. */
    fun askRemove(item: CollectionItem?) = _state.update { it.copy(itemToRemove = item) }

    /** Removes the confirmed item and updates the list and count locally. */
    fun removeItem() {
        val item = _state.value.itemToRemove ?: return
        _state.update { it.copy(itemToRemove = null) }
        viewModelScope.launch {
            when (val result = collectionRepository.setInCollection(item.media, collectionId, add = false)) {
                is ApiResult.Success -> _state.update { state ->
                    state.copy(
                        items = state.items.filterNot { it.dbMediaId == item.dbMediaId },
                        collection = state.collection?.let { it.copy(mediaCount = (it.mediaCount - 1).coerceAtLeast(0)) },
                    )
                }
                is ApiResult.Error -> _events.send(CollectionEvent.Message(result.message))
            }
        }
    }

    /** Opens or closes the edit dialog. */
    fun showEditDialog(show: Boolean) = _state.update { it.copy(showEditDialog = show, editError = null) }

    /** Saves the edit dialog's name and visibility. */
    fun save(name: String, visibility: Visibility) {
        val collection = _state.value.collection ?: return
        _state.update { it.copy(isSaving = true, editError = null) }
        viewModelScope.launch {
            when (val result = collectionRepository.update(collection, name, visibility)) {
                is ApiResult.Success -> _state.update {
                    it.copy(collection = result.data, isSaving = false, showEditDialog = false)
                }
                is ApiResult.Error -> _state.update { it.copy(isSaving = false, editError = result.message) }
            }
        }
    }

    /** Opens or closes the delete confirmation. */
    fun askDelete(ask: Boolean) = _state.update { it.copy(askDelete = ask) }

    /** Deletes the collection; [CollectionEvent.Deleted] follows on success. */
    fun delete() {
        _state.update { it.copy(isDeleting = true) }
        viewModelScope.launch {
            val result = collectionRepository.delete(collectionId)
            _state.update { it.copy(isDeleting = false, askDelete = false) }
            when (result) {
                is ApiResult.Success -> _events.send(CollectionEvent.Deleted)
                is ApiResult.Error -> _events.send(CollectionEvent.Message(result.message))
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                CollectionViewModel(createSavedStateHandle().toRoute<CollectionRoute>().collectionId, c.collectionRepository)
            }
        }
    }
}
