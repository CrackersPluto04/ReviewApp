package com.reviewapp.android.ui.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.ReplyRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.TmdbDiscoverFilters
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** One independently loaded block of the home page. */
data class Section<T>(
    val items: List<T> = emptyList(),
    val isLoading: Boolean = true,
    val error: String? = null,
)

private fun <T> ApiResult<List<T>>.toSection(): Section<T> = when (this) {
    is ApiResult.Success -> Section(data, isLoading = false)
    is ApiResult.Error -> Section(isLoading = false, error = message)
}

data class HomeUiState(
    val popularMovies: Section<Media> = Section(),
    val popularSeries: Section<Media> = Section(),
    val recentReviews: Section<FeedReview> = Section(),
    val isRefreshing: Boolean = false,
)

class HomeViewModel(
    private val mediaRepository: MediaRepository,
    private val reviewRepository: ReviewRepository,
    private val sessionManager: SessionManager,
    private val replyRepository: ReplyRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    init {
        load()
        // A newly written review shows up in the feed
        viewModelScope.launch { reviewRepository.changes.collect { loadReviews() } }
        // Replies written on the review page update the cards' counts without a reload
        viewModelScope.launch {
            replyRepository.replyCountChanges.collect { change ->
                _state.update { state ->
                    val section = state.recentReviews
                    state.copy(
                        recentReviews = section.copy(
                            items = section.items.map {
                                if (it.id == change.reviewId) it.copy(replyCount = (it.replyCount + change.delta).coerceAtLeast(0)) else it
                            },
                        ),
                    )
                }
            }
        }
    }

    fun refresh() {
        _state.update { it.copy(isRefreshing = true) }
        load()
        // The app may have started offline, a refresh is a good moment to check the session again
        viewModelScope.launch { sessionManager.restoreIfNeeded() }
    }

    private fun load() {
        viewModelScope.launch {
            val movies = async { popular(MediaType.Movie) }
            val series = async { popular(MediaType.Series) }
            val reviews = async { reviewRepository.recent(RECENT_REVIEW_COUNT) }
            _state.update {
                it.copy(
                    popularMovies = movies.await().toSection(),
                    popularSeries = series.await().toSection(),
                    recentReviews = reviews.await().toSection(),
                    isRefreshing = false,
                )
            }
        }
    }

    private fun loadReviews() {
        viewModelScope.launch {
            val reviews = reviewRepository.recent(RECENT_REVIEW_COUNT).toSection()
            _state.update { it.copy(recentReviews = reviews) }
        }
    }

    private suspend fun popular(type: MediaType): ApiResult<List<Media>> =
        when (val result = mediaRepository.discover(type, TmdbDiscoverFilters(), page = 1)) {
            is ApiResult.Success -> ApiResult.Success(result.data.items.take(CAROUSEL_SIZE))
            is ApiResult.Error -> result
        }

    companion object {
        private const val CAROUSEL_SIZE = 15
        private const val RECENT_REVIEW_COUNT = 10

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                HomeViewModel(c.mediaRepository, c.reviewRepository, c.sessionManager, c.replyRepository)
            }
        }
    }
}
