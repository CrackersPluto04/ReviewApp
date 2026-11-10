package com.reviewapp.android.ui

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.domain.model.Page
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** What a list screen shows of a [PagedLoader]: the items loaded so far and the state of the next load. */
data class PagedList<T>(
    val items: List<T> = emptyList(),
    /** The last page that loaded (1-based); 0 while nothing has loaded yet. */
    val page: Int = 0,
    /** The server's total over all pages, lowered locally when an item is removed. */
    val totalCount: Int = 0,
    val endReached: Boolean = false,
    val isLoading: Boolean = false,
    /** The message of the failed load, shown with a retry; `null` when the last load worked. */
    val error: String? = null,
    /** The next page needs a connection: the list stops at what the device has, without an error. */
    val offlineEnd: Boolean = false,
    /** The items are the device's offline copy from this time (epoch millis), not a fresh answer. */
    val updatedAt: Long? = null,
) {
    /** The first page is loading, so the screen shows a spinner instead of the list. */
    val isInitialLoading: Boolean get() = isLoading && items.isEmpty()

    /** A load finished and found nothing (not "not loaded yet", not failed). */
    val isEmpty: Boolean get() = items.isEmpty() && !isLoading && error == null && page > 0
}

/**
 * Infinite scroll over the API's page-based endpoints. [fetch] gets the page number to load and is
 * read again for every page, so it can use the current filters; call [reload] when they change.
 * Items with an already seen [itemKey] are dropped (Spotify repeats tracks across pages).
 */
class PagedLoader<T>(
    private val scope: CoroutineScope,
    private val itemKey: (T) -> Any,
    private val fetch: suspend (page: Int) -> ApiResult<Page<T>>,
) {
    private val _state = MutableStateFlow(PagedList<T>())

    /** The list as it is now; collected by the screen. */
    val state: StateFlow<PagedList<T>> = _state.asStateFlow()

    private var job: Job? = null

    /** Starts over from the first page, cancelling a load in progress. */
    fun reload() {
        job?.cancel()
        _state.value = PagedList(isLoading = true)
        job = scope.launch { load(1) }
    }

    /** Forgets everything; the next [loadIfNeeded] starts over. */
    fun reset() {
        job?.cancel()
        _state.value = PagedList()
    }

    /** Loads the first page only if nothing was loaded yet. */
    fun loadIfNeeded() {
        if (_state.value.page == 0 && !_state.value.isLoading) reload()
    }

    /** Loads the next page; does nothing while a load runs, at the end, or after an error (that needs [retry]). */
    fun loadMore() {
        val current = _state.value
        if (current.isLoading || current.endReached || current.error != null) return
        _state.update { it.copy(isLoading = true) }
        job = scope.launch { load(current.page + 1) }
    }

    /** Retries the page that failed (the first one when the list is still empty), or continues an offline end. */
    fun retry() {
        val current = _state.value
        if (current.page == 0) return reload()
        _state.update { it.copy(error = null, isLoading = true, offlineEnd = false, endReached = false) }
        job = scope.launch { load(current.page + 1) }
    }

    /** Changes one item locally (e.g. its reply count) instead of reloading the list. */
    fun update(key: Any, transform: (T) -> T) = _state.update { current ->
        current.copy(items = current.items.map { if (itemKey(it) == key) transform(it) else it })
    }

    /** Drops an item locally (e.g. after deleting it) instead of reloading the list. */
    fun remove(key: Any) = _state.update { current ->
        val remaining = current.items.filterNot { itemKey(it) == key }
        if (remaining.size == current.items.size) current
        else current.copy(items = remaining, totalCount = (current.totalCount - 1).coerceAtLeast(0))
    }

    private suspend fun load(page: Int) {
        when (val result = fetch(page)) {
            // Without an answer after the first page the list stops at what is there instead of failing; on the
            // first page there is nothing to show, so that is an error
            is ApiResult.Error -> _state.update {
                when {
                    result.isNetwork && page > 1 -> it.copy(isLoading = false, endReached = true, offlineEnd = true)
                    result.isNetwork -> it.copy(isLoading = false, error = OFFLINE_NOT_SAVED)
                    else -> it.copy(isLoading = false, error = result.message)
                }
            }
            is ApiResult.Success -> _state.update { current ->
                val base = if (page == 1) emptyList() else current.items
                val seen = base.mapTo(HashSet()) { itemKey(it) }
                val newItems = result.data.items.filter { seen.add(itemKey(it)) }
                current.copy(
                    items = base + newItems,
                    page = page,
                    totalCount = result.data.totalCount,
                    endReached = !result.data.hasMore || result.data.items.isEmpty(),
                    isLoading = false,
                    error = null,
                    offlineEnd = false,
                    // The oldest copy shown decides how old the list is
                    updatedAt = if (page == 1) result.cachedAt else listOfNotNull(current.updatedAt, result.cachedAt).minOrNull(),
                )
            }
        }
    }

    companion object {
        /** A list without a connection and without an offline copy. */
        const val OFFLINE_NOT_SAVED = "You're offline, and this list isn't saved on this phone. It loads when you're back online."
    }
}
