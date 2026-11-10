package com.reviewapp.android.ui.search

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.toRoute
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.PagedLoader
import com.reviewapp.android.ui.navigation.SearchRoute
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** "Any" shows the combined search, the others are infinite lists of one media type. */
enum class SearchTab(val label: String, val mediaType: MediaType?) {
    Any("Any", null),
    Movies("Movies", MediaType.Movie),
    Series("Series", MediaType.Series),
    Music("Music", MediaType.Music),
}

/** The "Any" tab's combined results. */
data class SearchAllState(
    val items: List<Media> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null,
    /** No connection: the results are matches from the offline library. */
    val offline: Boolean = false,
)

/** What the search screen shows. */
data class SearchUiState(
    /** The text in the field, which only becomes [query] on submit. */
    val draftQuery: String,
    /** The query the results belong to. */
    val query: String,
    val tab: SearchTab = SearchTab.Any,
    val all: SearchAllState = SearchAllState(),
)

/**
 * Media search for the query of the route: the combined "Any" results plus one infinite list per media type, each
 * loaded only when its tab is opened.
 */
class SearchViewModel(
    savedStateHandle: SavedStateHandle,
    private val mediaRepository: MediaRepository,
    private val reviewRepository: ReviewRepository,
) : ViewModel() {

    private val initialQuery = savedStateHandle.toRoute<SearchRoute>().query

    private val _state = MutableStateFlow(SearchUiState(draftQuery = initialQuery, query = initialQuery))
    val state: StateFlow<SearchUiState> = _state.asStateFlow()

    val scoreStats: StateFlow<Map<String, ScoreStats>> = reviewRepository.scoreStats

    /** One loader per media type tab, each reads the current query when it fetches. */
    private val loaders: Map<SearchTab, PagedLoader<Media>> = SearchTab.entries
        .mapNotNull { tab -> tab.mediaType?.let { type -> tab to createLoader(type) } }
        .toMap()

    /** The list of a media type tab ([SearchTab.Any] has none, it uses [SearchUiState.all]). */
    fun listState(tab: SearchTab): StateFlow<PagedList<Media>> = loaders.getValue(tab).state

    private var allJob: Job? = null

    init {
        loadAll()
    }

    private fun createLoader(type: MediaType) = PagedLoader<Media>(viewModelScope, itemKey = { it.key }) { page ->
        mediaRepository.search(type, _state.value.query, page)
    }

    fun onDraftQueryChange(value: String) = _state.update { it.copy(draftQuery = value) }

    /** Searches for the typed text (ignored when blank) and restarts the visible tab. */
    fun submit() {
        val query = _state.value.draftQuery.trim()
        if (query.isBlank()) return
        _state.update { it.copy(query = query, draftQuery = query) }
        loadAll()
        // Only the visible tab loads now, the others when they are opened
        loaders.forEach { (tab, loader) -> if (tab == _state.value.tab) loader.reload() else loader.reset() }
    }

    /** Opens a tab and loads its list the first time. */
    fun selectTab(tab: SearchTab) {
        _state.update { it.copy(tab = tab) }
        loaders[tab]?.loadIfNeeded()
    }

    fun loadMore(tab: SearchTab) = loaders[tab]?.loadMore()
    fun retry(tab: SearchTab) = loaders[tab]?.retry()

    fun retryAll() = loadAll()

    fun requestStats(media: Media) = reviewRepository.requestStats(media.mediaType, media.externalApiId)

    private fun loadAll() {
        allJob?.cancel()
        _state.update { it.copy(all = SearchAllState(isLoading = true)) }
        allJob = viewModelScope.launch {
            val all = when (val result = mediaRepository.searchAll(_state.value.query)) {
                is ApiResult.Success -> SearchAllState(items = result.data, offline = result.cachedAt != null)
                is ApiResult.Error -> SearchAllState(error = result.message)
            }
            _state.update { it.copy(all = all) }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                SearchViewModel(createSavedStateHandle(), c.mediaRepository, c.reviewRepository)
            }
        }
    }
}
