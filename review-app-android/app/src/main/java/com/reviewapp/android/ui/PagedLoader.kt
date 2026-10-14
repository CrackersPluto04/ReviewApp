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

data class PagedList<T>(
    val items: List<T> = emptyList(),
    val page: Int = 0,
    val totalCount: Int = 0,
    val endReached: Boolean = false,
    val isLoading: Boolean = false,
    val error: String? = null,
) {
    val isInitialLoading: Boolean get() = isLoading && items.isEmpty()
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

    fun loadMore() {
        val current = _state.value
        if (current.isLoading || current.endReached || current.error != null) return
        _state.update { it.copy(isLoading = true) }
        job = scope.launch { load(current.page + 1) }
    }

    /** Retries the page that failed (the first one when the list is still empty). */
    fun retry() {
        val current = _state.value
        if (current.page == 0) return reload()
        _state.update { it.copy(error = null, isLoading = true) }
        job = scope.launch { load(current.page + 1) }
    }

    private suspend fun load(page: Int) {
        when (val result = fetch(page)) {
            is ApiResult.Error -> _state.update { it.copy(isLoading = false, error = result.message) }
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
                )
            }
        }
    }
}
