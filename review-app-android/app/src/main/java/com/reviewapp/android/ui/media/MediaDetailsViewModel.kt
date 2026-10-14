package com.reviewapp.android.ui.media

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.toRoute
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.domain.model.mediaKey
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.PagedLoader
import com.reviewapp.android.ui.navigation.MediaDetailsRoute
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** What the write/edit button should do. */
enum class OwnReviewState { LoggedOut, Loading, None, Exists }

data class MediaDetailsUiState(
    val media: Media? = null,
    val isLoadingMedia: Boolean = true,
    val mediaError: String? = null,
    val filter: ReviewFilter = ReviewFilter(),
    val ownReview: OwnReviewState = OwnReviewState.Loading,
)

class MediaDetailsViewModel(
    savedStateHandle: SavedStateHandle,
    private val mediaRepository: MediaRepository,
    private val reviewRepository: ReviewRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    private val route = savedStateHandle.toRoute<MediaDetailsRoute>()
    val mediaType = MediaType.fromId(route.mediaType)
    val externalApiId = route.externalApiId
    private val key = mediaKey(mediaType, externalApiId)

    private val _state = MutableStateFlow(MediaDetailsUiState())
    val state: StateFlow<MediaDetailsUiState> = _state.asStateFlow()

    val stats: StateFlow<ScoreStats?> = reviewRepository.scoreStats
        .map { it[key] }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), reviewRepository.scoreStats.value[key])

    private val reviewsLoader = PagedLoader<MediaReview>(viewModelScope, itemKey = { it.id }) { page ->
        reviewRepository.mediaReviews(mediaType, externalApiId, _state.value.filter, page)
    }
    val reviews: StateFlow<PagedList<MediaReview>> = reviewsLoader.state

    private val _messages = Channel<String>(Channel.BUFFERED)
    val messages = _messages.receiveAsFlow()

    private var ownReviewJob: Job? = null

    init {
        loadMedia()
        reviewRepository.requestStats(mediaType, externalApiId)
        reviewsLoader.reload()

        // The button depends on who is logged in, so it's rechecked on every login/logout
        viewModelScope.launch {
            sessionManager.state
                .map { it is AuthState.LoggedIn }
                .distinctUntilChanged()
                .collect { loggedIn -> if (loggedIn) loadOwnReview() else setOwnReview(OwnReviewState.LoggedOut) }
        }

        viewModelScope.launch {
            reviewRepository.changes.filter { it.mediaKey == key }.collect { change ->
                reviewsLoader.reload()
                loadOwnReview()
                change.message?.let { _messages.send(it) }
            }
        }
    }

    fun loadMedia() {
        _state.update { it.copy(isLoadingMedia = true, mediaError = null) }
        viewModelScope.launch {
            when (val result = mediaRepository.getMedia(mediaType, externalApiId)) {
                is ApiResult.Success -> _state.update { it.copy(media = result.data, isLoadingMedia = false) }
                is ApiResult.Error -> _state.update { it.copy(isLoadingMedia = false, mediaError = result.message) }
            }
        }
    }

    private fun loadOwnReview() {
        ownReviewJob?.cancel()
        setOwnReview(OwnReviewState.Loading)
        ownReviewJob = viewModelScope.launch {
            val result = reviewRepository.ownReview(mediaType, externalApiId)
            // If the check fails the form loads it again anyway, so "write" is a safe fallback
            setOwnReview(
                if (result is ApiResult.Success && result.data != null) OwnReviewState.Exists else OwnReviewState.None,
            )
        }
    }

    private fun setOwnReview(value: OwnReviewState) = _state.update { it.copy(ownReview = value) }

    fun updateFilter(filter: ReviewFilter) {
        if (filter == _state.value.filter) return
        _state.update { it.copy(filter = filter) }
        reviewsLoader.reload()
    }

    fun loadMoreReviews() = reviewsLoader.loadMore()
    fun retryReviews() = reviewsLoader.retry()

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                MediaDetailsViewModel(createSavedStateHandle(), c.mediaRepository, c.reviewRepository, c.sessionManager)
            }
        }
    }
}
