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

/** The review page: the review, its reply thread and the reply box and dialogs. */
data class ReviewUiState(
    val detail: ReviewDetail? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    /** The review is deleted or hidden from this user (the API answers 404 for both). */
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

    /** The reply bar is shown: logged in and the review accepts replies. */
    val canReply: Boolean get() = isLoggedIn && isReplyable

    /** Only a too long draft shows an error while typing; an empty one just disables sending. */
    val draftError: String? get() = if (draft.trim().length > Validation.MAX_REPLY_LENGTH) Validation.replyError(draft) else null
    val canSend: Boolean get() = canReply && !isPosting && Validation.replyError(draft) == null
}

/** One-off results of the review page. */
sealed interface ReviewEvent {
    /** A failure to show on the snackbar. */
    data class Message(val text: String) : ReviewEvent

    /** The linked reply is on screen; scroll it into view. */
    data class ScrollToReply(val replyId: Int) : ReviewEvent

    /** The review was deleted, so the page closes. */
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

    /** The reply the route links to (from a notification or deep link), highlighted and scrolled to. */
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

    /**
     * Loads the review and, when the route links a reply, that reply with its parent so the thread opens there.
     * The first page of replies follows.
     */
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

    /** Loads the next page of the review's replies (layer 2). */
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

    /** Loads the next page of the replies to the layer-2 reply [parentId] (layer 3). */
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

    /** Stores the reply text and clears the post error. */
    fun updateDraft(text: String) = _state.update { it.copy(draft = text, postError = null) }

    /** Answers the layer-2 [reply] (`null`: back to replying to the review itself). */
    fun setReplyTarget(reply: Reply?) = _state.update { it.copy(replyTarget = reply, postError = null) }

    /** Posts the draft as a reply to the review or to the reply target and adds it to the thread. */
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

    /** Asks for confirmation to delete [reply]; `null` cancels. */
    fun askDeleteReply(reply: Reply?) = _state.update { it.copy(replyToDelete = reply) }

    /** Deletes the confirmed reply; the thread keeps a placeholder when it has children (see [ReplyThreadState.withDeleted]). */
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

    /** Opens or closes the review's delete confirmation. */
    fun askDeleteReview(ask: Boolean) = _state.update { it.copy(askDeleteReview = ask) }

    /** Deletes the review (and with it its replies); [ReviewEvent.ReviewDeleted] follows on success. */
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
