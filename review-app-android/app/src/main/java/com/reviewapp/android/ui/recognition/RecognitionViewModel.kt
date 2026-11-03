package com.reviewapp.android.ui.recognition

import android.net.Uri
import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.RecognitionPhotos
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.RecognitionRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.ScoreStats
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.io.File

data class RecognitionUiState(
    val isLoggedIn: Boolean = false,
    /** Null while loading. */
    val allowance: RecognitionAllowance? = null,
    val allowanceError: String? = null,
    val step: RecognitionStep = RecognitionStep.Start,
    val error: String? = null,
    /** Why the camera can't be used (no camera, permission denied): only picking a photo is offered. */
    val cameraMessage: String? = null,
    val results: RecognitionResults? = null,
    /** A picked or taken photo is being made smaller. */
    val isPreparing: Boolean = false,
    val noticeDismissed: Boolean = true,
) {
    val outOfRecognitions: Boolean get() = allowance?.remaining == 0
}

/**
 * Camera recognition (the web's `RecognitionPage`): take or pick a photo, send it, page through the suggestions.
 * The step, the photo and the shown results survive rotation (ViewModel) and process death (the saved state keeps
 * the photo's path and the recognition id, the list is reloaded from the API).
 */
class RecognitionViewModel(
    private val repository: RecognitionRepository,
    private val photos: RecognitionPhotos,
    private val reviewRepository: ReviewRepository,
    private val sessionManager: SessionManager,
    private val settingsStore: SettingsStore,
    private val savedState: SavedStateHandle,
) : ViewModel() {

    private val _state = MutableStateFlow(RecognitionUiState())
    val state: StateFlow<RecognitionUiState> = _state.asStateFlow()

    val scoreStats: StateFlow<Map<String, ScoreStats>> = reviewRepository.scoreStats

    private var recognizeJob: Job? = null

    init {
        viewModelScope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    // Another (or no) user never sees the previous one's photo or results
                    if (_state.value.isLoggedIn) reset()
                    _state.update { it.copy(isLoggedIn = userId != null, allowance = null) }
                    if (userId != null) {
                        loadAllowance()
                        restore()
                    }
                }
        }
        viewModelScope.launch {
            settingsStore.recognitionNoticeDismissed.collect { dismissed -> _state.update { it.copy(noticeDismissed = dismissed) } }
        }
    }

    fun loadAllowance() {
        _state.update { it.copy(allowanceError = null) }
        viewModelScope.launch {
            when (val result = repository.allowance()) {
                is ApiResult.Success -> _state.update { it.copy(allowance = result.data) }
                is ApiResult.Error -> _state.update { it.copy(allowanceError = result.message) }
            }
        }
    }

    fun requestStats(media: Media) = reviewRepository.requestStats(media.mediaType, media.externalApiId)

    fun dismissNotice() {
        viewModelScope.launch { settingsStore.dismissRecognitionNotice() }
    }

    fun openCamera() = _state.update { it.copy(step = RecognitionStep.Camera, error = null) }

    fun onCameraUnavailable(message: String) =
        _state.update { it.copy(step = RecognitionStep.Start, cameraMessage = message) }

    /** The camera permission was granted after all (e.g. in the system settings). */
    fun onCameraAvailable() = _state.update { it.copy(cameraMessage = null) }

    /** The cache file the camera writes the next photo to. */
    fun newCaptureFile(): File = photos.newCaptureFile()

    fun onPhotoCaptured(file: File) = prepare(Uri.fromFile(file), fromCamera = true) { file.delete() }

    fun onPhotoPicked(uri: Uri) = prepare(uri, fromCamera = false) {}

    private fun prepare(source: Uri, fromCamera: Boolean, cleanup: () -> Unit) {
        _state.update { it.copy(isPreparing = true, error = null) }
        viewModelScope.launch {
            val prepared = photos.prepare(source)
            cleanup()
            if (prepared == null) {
                _state.update {
                    it.copy(isPreparing = false, step = RecognitionStep.Start, error = "This photo couldn't be read. Choose a JPEG or PNG photo.")
                }
                return@launch
            }
            showPreview(prepared.path, fromCamera)
        }
    }

    private fun showPreview(path: String, fromCamera: Boolean) {
        savedState[KEY_PHOTO] = path
        savedState[KEY_FROM_CAMERA] = fromCamera
        _state.update { it.copy(isPreparing = false, step = RecognitionStep.Preview(path, fromCamera), error = null) }
    }

    fun retake() {
        val step = _state.value.step as? RecognitionStep.Preview ?: return
        discardPhoto()
        _state.update {
            it.copy(step = if (step.fromCamera && it.cameraMessage == null) RecognitionStep.Camera else RecognitionStep.Start, error = null)
        }
    }

    fun usePhoto() {
        val step = _state.value.step as? RecognitionStep.Preview ?: return
        if (_state.value.outOfRecognitions || recognizeJob?.isActive == true) return
        _state.update { it.copy(step = RecognitionStep.Recognizing(step.photoPath, step.fromCamera), error = null) }

        recognizeJob = viewModelScope.launch {
            when (val result = repository.recognize(File(step.photoPath))) {
                is ApiResult.Success -> {
                    // The photo is not kept once it was sent
                    discardPhoto()
                    val results = RecognitionResults.from(result.data)
                    saveResults(results)
                    _state.update { it.copy(step = RecognitionStep.Results, results = results, allowance = result.data.allowance) }
                }
                is ApiResult.Error -> when {
                    // The upsell dialog opens by itself (the 402 interceptor); this screen turns into the Free state
                    result.code == 402 -> {
                        discardPhoto()
                        _state.update { s -> s.copy(step = RecognitionStep.Start, allowance = s.allowance?.copy(premium = false)) }
                    }
                    else -> _state.update { s ->
                        s.copy(
                            step = step,
                            error = result.message,
                            allowance = if (result.reason == RecognitionReasons.DAILY_LIMIT) s.allowance?.copy(remaining = 0) else s.allowance,
                        )
                    }
                }
            }
        }
    }

    /** Stops waiting; whatever the API answers is discarded. */
    fun cancel() {
        val step = _state.value.step as? RecognitionStep.Recognizing ?: return
        recognizeJob?.cancel()
        _state.update { it.copy(step = RecognitionStep.Preview(step.photoPath, step.fromCamera)) }
    }

    fun showMore() {
        val results = _state.value.results ?: return
        val id = results.recognitionId ?: return
        if (!results.hasMore || results.isLoadingMore) return
        _state.update { it.copy(results = results.loadingMore()) }

        viewModelScope.launch {
            val result = repository.items(id, results.nextOffset)
            _state.update { s ->
                val current = s.results ?: return@update s
                s.copy(
                    results = when {
                        result is ApiResult.Success -> current.merged(result.data)
                        result is ApiResult.Error && result.reason == RecognitionReasons.NOT_FOUND -> current.expired()
                        // 402: the upsell opens by itself and the list stays
                        result is ApiResult.Error && result.code == 402 -> current.copy(isLoadingMore = false)
                        else -> current.failed((result as ApiResult.Error).message)
                    },
                )
            }
            _state.value.results?.let(::saveResults)
        }
    }

    fun retryMore() {
        _state.update { s -> s.copy(results = s.results?.retried()) }
        showMore()
    }

    /** Back to the first step for a new photo. */
    fun startOver() {
        recognizeJob?.cancel()
        discardPhoto()
        clearSavedResults()
        _state.update { it.copy(step = RecognitionStep.Start, results = null, error = null) }
    }

    /** The system back action: the flow steps back first, true when it did. */
    fun back(): Boolean = when (_state.value.step) {
        RecognitionStep.Start -> false
        is RecognitionStep.Recognizing -> {
            cancel()
            true
        }
        else -> {
            startOver()
            true
        }
    }

    private fun reset() {
        recognizeJob?.cancel()
        discardPhoto()
        clearSavedResults()
        _state.update { it.copy(step = RecognitionStep.Start, results = null, error = null, cameraMessage = null) }
    }

    private fun discardPhoto() {
        photos.delete(savedState[KEY_PHOTO])
        savedState.remove<String>(KEY_PHOTO)
        savedState.remove<Boolean>(KEY_FROM_CAMERA)
    }

    // Only what reloading needs: the id, how many items were shown, and what was said about the photo
    private fun saveResults(results: RecognitionResults) {
        val id = results.recognitionId ?: return clearSavedResults()
        savedState[KEY_RECOGNITION_ID] = id
        savedState[KEY_SHOWN] = results.items.size
        savedState[KEY_SUMMARY] = results.summary
        savedState[KEY_OUTCOME] = results.outcome.apiName
        savedState[KEY_INCOMPLETE] = results.incomplete
    }

    private fun clearSavedResults() {
        listOf(KEY_RECOGNITION_ID, KEY_SHOWN, KEY_SUMMARY, KEY_OUTCOME, KEY_INCOMPLETE).forEach { savedState.remove<Any>(it) }
    }

    // After process death: the photo on screen (its file is still in the cache) or the shown suggestions
    private fun restore() {
        if (_state.value.step != RecognitionStep.Start) return

        val photo = savedState.get<String>(KEY_PHOTO)
        if (photo != null && File(photo).exists()) {
            showPreview(photo, savedState[KEY_FROM_CAMERA] ?: false)
            return
        }
        // Nothing to restore: leftovers of an earlier visit go
        if (photo == null) photos.clear()

        val id = savedState.get<String>(KEY_RECOGNITION_ID) ?: return
        val shown = savedState.get<Int>(KEY_SHOWN) ?: return
        val placeholder = RecognitionResults(
            recognitionId = id,
            outcome = RecognitionOutcome.fromApi(savedState[KEY_OUTCOME]),
            summary = savedState[KEY_SUMMARY] ?: "",
            items = emptyList(),
            hasMore = true,
            nextOffset = 0,
            incomplete = savedState[KEY_INCOMPLETE] ?: false,
            isLoadingMore = true,
        )
        _state.update { it.copy(step = RecognitionStep.Results, results = placeholder) }
        viewModelScope.launch {
            val result = repository.items(id, 0, shown)
            _state.update { s ->
                val current = s.results ?: return@update s
                s.copy(results = if (result is ApiResult.Success) current.merged(result.data) else current.expired())
            }
        }
    }

    companion object {
        private const val KEY_PHOTO = "recognition_photo"
        private const val KEY_FROM_CAMERA = "recognition_from_camera"
        private const val KEY_RECOGNITION_ID = "recognition_id"
        private const val KEY_SHOWN = "recognition_shown"
        private const val KEY_SUMMARY = "recognition_summary"
        private const val KEY_OUTCOME = "recognition_outcome"
        private const val KEY_INCOMPLETE = "recognition_incomplete"

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                RecognitionViewModel(
                    c.recognitionRepository, c.recognitionPhotos, c.reviewRepository, c.sessionManager, c.settingsStore,
                    createSavedStateHandle(),
                )
            }
        }
    }
}
