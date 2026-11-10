package com.reviewapp.android.ui.notifications

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.NotificationRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** The notifications screen: the filter chips, the sort and the loaded list. */
data class NotificationsUiState(
    val isLoggedIn: Boolean = true,
    /** The Seen / Unseen chips; exactly one on filters, both or neither means all. */
    val seenOn: Boolean = false,
    val unseenOn: Boolean = false,
    val sort: NotificationSort = NotificationSort.Newest,
    val list: NotificationListState = NotificationListState(),
    val askDeleteSeen: Boolean = false,
    /** Mark all seen / delete all seen in progress. */
    val isBusy: Boolean = false,
) {
    /** The API filter the chips stand for. */
    val status: NotificationStatus get() = NotificationStatus.from(seenOn, unseenOn)
}

/** One-off results of the notifications screen. */
sealed interface NotificationsEvent {
    /** A failure to show on the snackbar. */
    data class Message(val text: String) : NotificationsEvent

    /** "Take me there": navigate to [target]. */
    data class Open(val target: NotificationTarget) : NotificationsEvent
}

/**
 * The web's `NotificationsDialog`: the current user's notifications with Seen / Unseen filters and
 * sorting. Every action changes the list and the badge at once and puts them back if it fails.
 */
class NotificationsViewModel(
    private val repository: NotificationRepository,
    private val sessionManager: SessionManager,
) : ViewModel() {

    private val _state = MutableStateFlow(NotificationsUiState())
    val state: StateFlow<NotificationsUiState> = _state.asStateFlow()

    /** The badge count, shown in the screen's title. */
    val unseenCount: StateFlow<Int> = repository.unseenCount

    private val _events = Channel<NotificationsEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    private var loadJob: Job? = null

    init {
        viewModelScope.launch {
            sessionManager.state
                .filter { it != AuthState.Loading }
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    _state.update { it.copy(isLoggedIn = userId != null, askDeleteSeen = false) }
                    if (userId != null) {
                        reload()
                    } else {
                        loadJob?.cancel()
                        updateList { NotificationListState() }
                    }
                }
        }
    }

    /** Starts over with the current filter; a page still loading for the old one is cancelled. */
    fun reload() {
        loadJob?.cancel()
        updateList { NotificationListState().loading() }
        loadPage()
    }

    /** Loads the next page when there is one. */
    fun loadMore() {
        if (!_state.value.list.canLoadMore) return
        updateList { it.loading() }
        loadPage()
    }

    /** Retries a failed page, or the first load when nothing has loaded. */
    fun retry() {
        if (!_state.value.list.loaded) return reload()
        updateList { it.loading() }
        loadPage()
    }

    private fun loadPage() {
        val current = _state.value
        loadJob = viewModelScope.launch {
            val result = repository.list(current.status, current.sort, current.list.cursor)
            updateList { list ->
                when (result) {
                    is ApiResult.Success -> list.merged(result.data).copy(updatedAt = result.cachedAt)
                    is ApiResult.Error -> list.failed(result.message)
                }
            }
        }
    }

    fun toggleSeenFilter() = updateFilter { it.copy(seenOn = !it.seenOn) }
    fun toggleUnseenFilter() = updateFilter { it.copy(unseenOn = !it.unseenOn) }
    fun selectSort(sort: NotificationSort) = updateFilter { it.copy(sort = sort) }

    private fun updateFilter(transform: (NotificationsUiState) -> NotificationsUiState) {
        val old = _state.value
        val new = transform(old)
        _state.value = new
        if (new.status != old.status || new.sort != old.sort) reload()
    }

    /** Marks a notification seen or unseen. Optimistic: the list and the badge change at once and revert on failure. */
    fun toggleSeen(notification: AppNotification) {
        val seen = !notification.isSeen
        updateList { it.withSeen(notification.id, seen) }
        repository.adjustUnseen(if (seen) -1 else 1)
        viewModelScope.launch {
            val result = repository.setSeen(notification.id, seen)
            if (result is ApiResult.Error) {
                updateList { it.withSeen(notification.id, !seen) }
                repository.adjustUnseen(if (seen) 1 else -1)
                _events.send(NotificationsEvent.Message(result.message))
            }
        }
    }

    /** Deletes a notification, optimistically; it is put back at its place when the request fails. */
    fun delete(notification: AppNotification) {
        val removal = _state.value.list.without(notification.id) ?: return
        updateList { removal.state }
        if (!notification.isSeen) repository.adjustUnseen(-1)
        viewModelScope.launch {
            val result = repository.delete(notification.id)
            if (result is ApiResult.Error) {
                updateList { it.restored(removal.item, removal.index) }
                if (!notification.isSeen) repository.adjustUnseen(1)
                _events.send(NotificationsEvent.Message(result.message))
            }
        }
    }

    /** Marks every notification seen, optimistically, and reloads when a Seen / Unseen filter is active. */
    fun markAllSeen() {
        val current = _state.value
        if (current.isBusy) return
        val previousList = current.list
        val previousCount = repository.unseenCount.value
        _state.update { it.copy(isBusy = true, list = it.list.allSeen()) }
        repository.setUnseen(0)
        viewModelScope.launch {
            val result = repository.markAllSeen()
            _state.update { it.copy(isBusy = false) }
            when (result) {
                // The Unseen filter's items no longer belong to it
                is ApiResult.Success -> if (_state.value.status != NotificationStatus.All) reload()
                is ApiResult.Error -> {
                    updateList { previousList }
                    repository.setUnseen(previousCount)
                    _events.send(NotificationsEvent.Message(result.message))
                }
            }
        }
    }

    /** Opens or closes the confirmation for [deleteSeen]. */
    fun askDeleteSeen(ask: Boolean) = _state.update { it.copy(askDeleteSeen = ask) }

    /** Deletes all seen notifications and reloads the list. */
    fun deleteSeen() {
        _state.update { it.copy(askDeleteSeen = false, isBusy = true) }
        viewModelScope.launch {
            val result = repository.deleteSeen()
            _state.update { it.copy(isBusy = false) }
            when (result) {
                is ApiResult.Success -> reload()
                is ApiResult.Error -> _events.send(NotificationsEvent.Message(result.message))
            }
        }
    }

    /** "Take me there": marks it seen without waiting (like the web) and opens its target. */
    fun open(notification: AppNotification) {
        val target = notificationTarget(notification, sessionManager.currentUser?.username) ?: return
        if (!notification.isSeen) {
            updateList { it.withSeen(notification.id, true) }
            repository.adjustUnseen(-1)
            viewModelScope.launch { repository.setSeen(notification.id, true) }
        }
        _events.trySend(NotificationsEvent.Open(target))
    }

    /** Whether "Take me there" applies, so the card shows the button only when it leads somewhere. */
    fun hasTarget(notification: AppNotification): Boolean =
        notificationTarget(notification, sessionManager.currentUser?.username) != null

    private fun updateList(transform: (NotificationListState) -> NotificationListState) =
        _state.update { it.copy(list = transform(it.list)) }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                NotificationsViewModel(c.notificationRepository, c.sessionManager)
            }
        }
    }
}
