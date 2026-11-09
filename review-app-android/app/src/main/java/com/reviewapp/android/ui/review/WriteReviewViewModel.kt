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
import com.reviewapp.android.data.sync.LocalLibrary
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
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
    /** The form shows text the user left unsaved earlier (the app was closed meanwhile). */
    val restoredDraft: Boolean = false,
) {
    val isReviewTooLong get() = draft.reviewText.length > Validation.MAX_REVIEW_LENGTH
    val isProsTooLong get() = draft.pros.length > Validation.MAX_PROS_CONS_LENGTH
    val isConsTooLong get() = draft.cons.length > Validation.MAX_PROS_CONS_LENGTH
    val canSave get() = media != null && !isLoading && !isSaving && !isReviewTooLong && !isProsTooLong && !isConsTooLong
}

/** A review form's unsaved text, kept in the offline copy's `drafts`. */
@Serializable
private data class StoredReviewDraft(val score: Double, val reviewText: String, val pros: String, val cons: String, val visibility: Int) {
    fun toDraft() = ReviewDraft(score, reviewText, pros, cons, Visibility.fromId(visibility))

    companion object {
        fun from(draft: ReviewDraft) = StoredReviewDraft(draft.score, draft.reviewText, draft.pros, draft.cons, draft.visibility.id)
    }
}

class WriteReviewViewModel(
    savedStateHandle: SavedStateHandle,
    private val mediaRepository: MediaRepository,
    private val reviewRepository: ReviewRepository,
    private val local: LocalLibrary,
) : ViewModel() {

    private val route = savedStateHandle.toRoute<WriteReviewRoute>()
    private val mediaType = MediaType.fromId(route.mediaType)
    private val draftKey = "review:${route.mediaType}:${route.externalApiId}"
    private var draftJob: Job? = null

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
            // Text the user left here when the app was closed comes back
            val stored = local.loadDraft(draftKey)?.let { json ->
                runCatching { Json.decodeFromString(StoredReviewDraft.serializer(), json).toDraft() }.getOrNull()
            }
            _state.update { current ->
                when {
                    media is ApiResult.Error -> current.copy(isLoading = false, loadError = media.message)
                    own is ApiResult.Error -> current.copy(isLoading = false, loadError = own.message)
                    else -> {
                        val existing = (own as ApiResult.Success).data
                        val loaded = existing?.let { it.copy(score = snapScore(it.score)) } ?: current.draft
                        current.copy(
                            media = (media as ApiResult.Success).data,
                            isLoading = false,
                            isEdit = existing != null,
                            draft = stored ?: loaded,
                            restoredDraft = stored != null && stored != loaded,
                        )
                    }
                }
            }
        }
    }

    fun updateDraft(change: ReviewDraft.() -> ReviewDraft) {
        _state.update { it.copy(draft = it.draft.change(), saveError = null) }
        // Kept on the device while typing (debounced), so a killed app doesn't lose it
        draftJob?.cancel()
        draftJob = viewModelScope.launch {
            delay(DRAFT_DELAY_MILLIS)
            local.saveDraft(draftKey, Json.encodeToString(StoredReviewDraft.serializer(), StoredReviewDraft.from(_state.value.draft)))
        }
    }

    /** Leaving the form on purpose (or after saving) throws its draft away; a killed app never gets here. */
    override fun onCleared() {
        draftJob?.cancel()
        CoroutineScope(Dispatchers.IO).launch { local.deleteDraft(draftKey) }
    }

    fun save() {
        val current = _state.value
        val media = current.media ?: return
        if (!current.canSave) return

        _state.update { it.copy(isSaving = true, saveError = null) }
        viewModelScope.launch {
            when (val result = reviewRepository.save(media, current.draft, current.isEdit)) {
                is ApiResult.Success -> {
                    draftJob?.cancel()
                    local.deleteDraft(draftKey)
                    _saved.send(Unit)
                }
                is ApiResult.Error -> _state.update { it.copy(isSaving = false, saveError = result.message) }
            }
        }
    }

    companion object {
        private const val DRAFT_DELAY_MILLIS = 500L

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                WriteReviewViewModel(createSavedStateHandle(), c.mediaRepository, c.reviewRepository, c.localLibrary)
            }
        }
    }
}
