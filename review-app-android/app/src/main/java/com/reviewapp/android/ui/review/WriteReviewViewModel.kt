package com.reviewapp.android.ui.review

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
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.navigation.WriteReviewRoute
import com.reviewapp.android.util.Validation
import com.reviewapp.android.util.snapScore
import kotlinx.coroutines.async
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class WriteReviewUiState(
    val media: Media? = null,
    val isLoading: Boolean = true,
    val loadError: String? = null,
    /** True when the user already has a review of this media, then saving edits it. */
    val isEdit: Boolean = false,
    val draft: ReviewDraft = ReviewDraft(
        score = 5.0,
        reviewText = "",
        pros = "",
        cons = "",
        visibility = Visibility.Public,
    ),
    val isSaving: Boolean = false,
    val saveError: String? = null,
) {
    val isReviewTooLong get() = draft.reviewText.length > Validation.MAX_REVIEW_LENGTH
    val isProsTooLong get() = draft.pros.length > Validation.MAX_PROS_CONS_LENGTH
    val isConsTooLong get() = draft.cons.length > Validation.MAX_PROS_CONS_LENGTH
    val canSave get() = media != null && !isLoading && !isSaving && !isReviewTooLong && !isProsTooLong && !isConsTooLong
}

class WriteReviewViewModel(
    savedStateHandle: SavedStateHandle,
    private val mediaRepository: MediaRepository,
    private val reviewRepository: ReviewRepository,
) : ViewModel() {

    private val route = savedStateHandle.toRoute<WriteReviewRoute>()
    private val mediaType = MediaType.fromId(route.mediaType)

    private val _state = MutableStateFlow(WriteReviewUiState())
    val state: StateFlow<WriteReviewUiState> = _state.asStateFlow()

    /** Fires once after a successful save so the screen can close. */
    private val _saved = Channel<Unit>(Channel.BUFFERED)
    val saved = _saved.receiveAsFlow()

    init {
        load()
    }

    /** Loads the media and the user's existing review (to switch to edit mode and prefill the form). */
    fun load() {
        _state.update { it.copy(isLoading = true, loadError = null) }
        viewModelScope.launch {
            val mediaResult = async { mediaRepository.getMedia(mediaType, route.externalApiId) }
            val ownResult = async { reviewRepository.ownReview(mediaType, route.externalApiId) }

            val media = mediaResult.await()
            val own = ownResult.await()
            _state.update { current ->
                when {
                    media is ApiResult.Error -> current.copy(isLoading = false, loadError = media.message)
                    own is ApiResult.Error -> current.copy(isLoading = false, loadError = own.message)
                    else -> {
                        val existing = (own as ApiResult.Success).data
                        current.copy(
                            media = (media as ApiResult.Success).data,
                            isLoading = false,
                            isEdit = existing != null,
                            draft = existing?.let { it.copy(score = snapScore(it.score)) } ?: current.draft,
                        )
                    }
                }
            }
        }
    }

    fun updateDraft(change: ReviewDraft.() -> ReviewDraft) {
        _state.update { it.copy(draft = it.draft.change(), saveError = null) }
    }

    fun save() {
        val current = _state.value
        val media = current.media ?: return
        if (!current.canSave) return

        _state.update { it.copy(isSaving = true, saveError = null) }
        viewModelScope.launch {
            when (val result = reviewRepository.save(media, current.draft, current.isEdit)) {
                is ApiResult.Success -> _saved.send(Unit)
                is ApiResult.Error -> _state.update { it.copy(isSaving = false, saveError = result.message) }
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                WriteReviewViewModel(createSavedStateHandle(), c.mediaRepository, c.reviewRepository)
            }
        }
    }
}
