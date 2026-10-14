package com.reviewapp.android.ui.review

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
import com.reviewapp.android.data.repository.ReplyRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.navigation.ReviewRoute
import com.reviewapp.android.util.Validation
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
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

data class ReviewUiState(
    val detail: ReviewDetail? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    val notFound: Boolean = false,
    /** The linked reply was deleted or isn't visible, the review is shown anyway. */
    val focusMissing: Boolean = false,
    val isLoggedIn: Boolean = false,
    val thread: ReplyThreadState = ReplyThreadState(),
    val draft: String = "",
    /** The layer-2 reply being answered, null when replying to the review. */
    val replyTarget: Reply? = null,
    val isPosting: Boolean = false,
    val postError: String? = null,
    val replyToDelete: Reply? = null,
    val askDeleteReview: Boolean = false,
    val isDeleting: Boolean = false,
) {
    /** Private reviews can't be replied to, not even by their owner. */
    val isReplyable: Boolean get() = detail != null && detail.review.visibility != Visibility.Private
    val canReply: Boolean get() = isLoggedIn && isReplyable
    val draftError: String? get() = if (draft.trim().length > Validation.MAX_REPLY_LENGTH) Validation.replyError(draft) else null
    val canSend: Boolean get() = canReply && !isPosting && Validation.replyError(draft) == null
}

sealed interface ReviewEvent {
    data class Message(val text: String) : ReviewEvent
    data class ScrollToReply(val replyId: Int) : ReviewEvent
    data object ReviewDeleted : ReviewEvent
}

/** The web's `ReviewPage`: one review with its reply thread, optionally opened at a reply. */
class ReviewViewModel(
    route: ReviewRoute,
    private val reviewRepository: ReviewRepository,
    private val replyRepository: ReplyRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    val reviewId = route.reviewId
    private val focusReplyId = route.replyId

    private val _state = MutableStateFlow(ReviewUiState())
    val state: StateFlow<ReviewUiState> = _state.asStateFlow()

    private val _events = Channel<ReviewEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    private var loadJob: Job? = null
    private var scrolledToFocus = false

    init {
        // Ownership, visibility and the reply box depend on who is logged in, so a login/logout reloads
        viewModelScope.launch {
            sessionManager.state
                .filter { it != AuthState.Loading }
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    _state.update { it.copy(isLoggedIn = userId != null, replyTarget = null, replyToDelete = null) }
                    load()
                }
        }
    }

    fun load() {
        loadJob?.cancel()
        _state.update { it.copy(isLoading = it.detail == null, error = null, notFound = false) }
        loadJob = viewModelScope.launch {
            val review = async { reviewRepository.review(reviewId) }
            val context = focusReplyId?.let { id -> async { replyRepository.context(id) } }
            val reviewResult = review.await()
            val contextResult = context?.await()

            if (reviewResult is ApiResult.Error) {
                _state.update { it.copy(isLoading = false, error = reviewResult.message, notFound = reviewResult.code == 404) }
                return@launch
            }
            val detail = (reviewResult as ApiResult.Success).data
            val focus = (contextResult as? ApiResult.Success)?.data?.takeIf { it.reviewId == reviewId }
            val thread = if (focus != null) {
                ReplyThreadState.seeded(focus, detail.review.replyCount)
            } else {
                ReplyThreadState(replyCount = detail.review.replyCount)
            }
            _state.update {
                it.copy(
                    detail = detail,
                    isLoading = false,
                    focusMissing = focusReplyId != null && focus == null,
                    thread = thread,
                )
            }
            loadTop()
            focus?.parentReply?.let { loadChildren(it.id) }
        }
    }

    fun loadTop() {
        val list = _state.value.thread.top
        if (list.isLoading) return
        updateThread { it.withTop { top -> top.loading() } }
        viewModelScope.launch {
            val result = replyRepository.replies(reviewId, parentReplyId = null, afterId = list.cursor)
            updateThread { thread ->
                thread.withTop { top ->
                    when (result) {
                        is ApiResult.Success -> top.merged(result.data)
                        is ApiResult.Error -> top.failed(result.message)
                    }
                }
            }
            scrollToFocusOnce()
        }
    }

    fun loadChildren(parentId: Int) {
        val list = _state.value.thread.childrenOf(parentId)
        if (list.isLoading) return
        updateThread { it.withChildren(parentId) { children -> children.loading() } }
        viewModelScope.launch {
            val result = replyRepository.replies(reviewId, parentReplyId = parentId, afterId = list.cursor)
            updateThread { thread ->
                thread.withChildren(parentId) { children ->
                    when (result) {
                        is ApiResult.Success -> children.merged(result.data)
                        is ApiResult.Error -> children.failed(result.message)
                    }
                }
            }
            scrollToFocusOnce()
        }
    }

    /** Shows or hides a reply's children, the first page loads on the first open. */
    fun toggleChildren(parent: Reply) {
        val wasExpanded = parent.id in _state.value.thread.expanded
        updateThread { it.toggled(parent.id) }
        val children = _state.value.thread.childrenOf(parent.id)
        if (!wasExpanded && !children.loaded && !children.isLoading) loadChildren(parent.id)
    }

    /** Once the pages around the linked reply arrived, so nothing loaded later pushes it out of view. */
    private fun scrollToFocusOnce() {
        val thread = _state.value.thread
        val id = thread.highlightId ?: return
        if (scrolledToFocus || thread.top.isLoading || thread.expanded.any { thread.childrenOf(it).isLoading }) return
        scrolledToFocus = true
        _events.trySend(ReviewEvent.ScrollToReply(id))
    }

    // Writing replies

    fun updateDraft(text: String) = _state.update { it.copy(draft = text, postError = null) }

    fun setReplyTarget(reply: Reply?) = _state.update { it.copy(replyTarget = reply, postError = null) }

    fun send() {
        val current = _state.value
        if (!current.canSend) return
        val parentId = current.replyTarget?.id
        _state.update { it.copy(isPosting = true, postError = null) }
        viewModelScope.launch {
            when (val result = replyRepository.create(reviewId, parentId, current.draft)) {
                is ApiResult.Success -> {
                    val childrenLoaded = parentId?.let { _state.value.thread.childrenOf(it).loaded }
                    _state.update {
                        it.copy(isPosting = false, draft = "", replyTarget = null, thread = it.thread.withAdded(result.data))
                    }
                    // Replying opens the parent's children; load the rest if they weren't loaded yet
                    if (parentId != null && childrenLoaded == false) loadChildren(parentId)
                }
                is ApiResult.Error -> _state.update { it.copy(isPosting = false, postError = result.message) }
            }
        }
    }

    // Deleting

    fun askDeleteReply(reply: Reply?) = _state.update { it.copy(replyToDelete = reply) }

    fun deleteReply() {
        val reply = _state.value.replyToDelete ?: return
        _state.update { it.copy(isDeleting = true) }
        viewModelScope.launch {
            val result = replyRepository.delete(reviewId, reply.id)
            _state.update { it.copy(isDeleting = false, replyToDelete = null) }
            when (result) {
                is ApiResult.Success -> {
                    updateThread { it.withDeleted(reply) }
                    if (_state.value.replyTarget?.id == reply.id) setReplyTarget(null)
                }
                is ApiResult.Error -> _events.send(ReviewEvent.Message(result.message))
            }
        }
    }

    fun askDeleteReview(ask: Boolean) = _state.update { it.copy(askDeleteReview = ask) }

    fun deleteReview() {
        val detail = _state.value.detail ?: return
        _state.update { it.copy(isDeleting = true) }
        viewModelScope.launch {
            val result = reviewRepository.delete(detail.review)
            _state.update { it.copy(isDeleting = false, askDeleteReview = false) }
            when (result) {
                is ApiResult.Success -> _events.send(ReviewEvent.ReviewDeleted)
                is ApiResult.Error -> _events.send(ReviewEvent.Message(result.message))
            }
        }
    }

    private fun updateThread(transform: (ReplyThreadState) -> ReplyThreadState) =
        _state.update { it.copy(thread = transform(it.thread)) }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                ReviewViewModel(
                    createSavedStateHandle().toRoute<ReviewRoute>(), c.reviewRepository, c.replyRepository, c.sessionManager,
                )
            }
        }
    }
}
