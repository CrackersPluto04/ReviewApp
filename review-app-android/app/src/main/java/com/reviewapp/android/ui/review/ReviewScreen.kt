package com.reviewapp.android.ui.review

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.InputChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.ProfileReviewCard
import com.reviewapp.android.ui.components.UserAvatar
import com.reviewapp.android.util.Validation
import com.reviewapp.android.util.formatDate
import com.reviewapp.android.util.pluralize

/** Rows above the thread in the list, so a reply's list index is this plus its row index. */
private const val HEADER_ROWS = 4

/** The web's `ReviewPage`: the review, its reply thread and a reply box at the bottom. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReviewScreen(
    onBack: () -> Unit,
    onLogin: () -> Unit,
    onOpenProfile: (String) -> Unit,
    onOpenMedia: (MediaType, String) -> Unit,
    onEditReview: (MediaType, String) -> Unit,
    viewModel: ReviewViewModel = viewModel(factory = ReviewViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val listState = rememberLazyListState()
    val replyFocus = remember { FocusRequester() }
    val currentOnBack by rememberUpdatedState(onBack)
    val rows = state.thread.rows()

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is ReviewEvent.Message -> snackbarHostState.showSnackbar(event.text)
                ReviewEvent.ReviewDeleted -> currentOnBack()
                is ReviewEvent.ScrollToReply -> {
                    // From the latest state: the event can arrive before the new rows are composed
                    val rows = viewModel.state.value.thread.rows()
                    val target = rows.filterIsInstance<ThreadRow.Item>().firstOrNull { it.reply.id == event.replyId }?.reply
                    // A reply to a reply is shown with its parent above it for context
                    val scrollToId = target?.parentReplyId ?: event.replyId
                    val index = rows.indexOfFirst { it is ThreadRow.Item && it.reply.id == scrollToId }
                    if (index >= 0) listState.animateScrollToItem(HEADER_ROWS + index)
                }
            }
        }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Review") }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        bottomBar = {
            if (state.detail != null && state.canReply) {
                ReplyBar(
                    state = state,
                    focusRequester = replyFocus,
                    onDraftChange = viewModel::updateDraft,
                    onClearTarget = { viewModel.setReplyTarget(null) },
                    onSend = viewModel::send,
                )
            }
        },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        val detail = state.detail
        when {
            detail == null && state.isLoading -> Box(modifier) { LoadingState() }
            detail == null -> Box(modifier) {
                if (state.notFound) {
                    EmptyState("This review doesn't exist or isn't visible to you.")
                } else {
                    ErrorState(state.error ?: "Could not load this review.", viewModel::load, Modifier.fillMaxWidth())
                }
            }
            else -> LazyColumn(
                state = listState,
                contentPadding = PaddingValues(bottom = 24.dp),
                modifier = modifier,
            ) {
                // Keep the number of items here in sync with HEADER_ROWS
                item(key = "author") { AuthorRow(detail, onOpenProfile) }
                item(key = "focus-missing") { if (state.focusMissing) FocusMissingNotice() }
                item(key = "review") {
                    ProfileReviewCard(
                        review = detail.review,
                        onClick = null,
                        onOpenMedia = { onOpenMedia(detail.review.mediaType, detail.review.externalApiId) },
                        onEdit = { onEditReview(detail.review.mediaType, detail.review.externalApiId) },
                        onDelete = { viewModel.askDeleteReview(true) },
                        modifier = Modifier.padding(horizontal = 16.dp),
                    )
                }
                item(key = "replies-header") { RepliesHeader(state, onLogin) }

                items(rows, key = { it.key }) { row ->
                    when (row) {
                        is ThreadRow.Item -> ReplyItem(
                            reply = row.reply,
                            highlighted = row.highlighted,
                            canReply = state.canReply && row.reply.isTopLevel,
                            isExpanded = row.reply.id in state.thread.expanded,
                            onOpenProfile = onOpenProfile,
                            onReply = {
                                viewModel.setReplyTarget(row.reply)
                                replyFocus.requestFocus()
                            },
                            onToggleChildren = { viewModel.toggleChildren(row.reply) },
                            onDelete = { viewModel.askDeleteReply(row.reply) },
                        )
                        is ThreadRow.Loading -> Box(
                            Modifier.fillMaxWidth().padding(12.dp).padding(start = indentOf(row.parentId)),
                            contentAlignment = Alignment.Center,
                        ) { CircularProgressIndicator(Modifier.size(24.dp)) }
                        is ThreadRow.Error -> ErrorState(
                            row.message,
                            onRetry = { if (row.parentId == null) viewModel.loadTop() else viewModel.loadChildren(row.parentId) },
                            modifier = Modifier.fillMaxWidth().padding(start = indentOf(row.parentId)),
                        )
                        is ThreadRow.LoadMore -> TextButton(
                            onClick = { if (row.parentId == null) viewModel.loadTop() else viewModel.loadChildren(row.parentId) },
                            modifier = Modifier.padding(start = 8.dp + indentOf(row.parentId)),
                        ) { Text("Load more replies") }
                        ThreadRow.Empty -> EmptyState("No replies yet.")
                    }
                }
            }
        }
    }

    ReviewDialogs(state, viewModel)
}

private fun indentOf(parentId: Int?) = if (parentId == null) 0.dp else 40.dp

private val CompactButtonPadding = PaddingValues(horizontal = 8.dp)

@Composable
private fun AuthorRow(detail: ReviewDetail, onOpenProfile: (String) -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable { onOpenProfile(detail.username) }
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        UserAvatar(detail.username, detail.profilePictureUrl)
        Spacer(Modifier.width(12.dp))
        Text(
            "${detail.username}'s review",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
        )
    }
}

@Composable
private fun FocusMissingNotice() {
    Surface(
        color = MaterialTheme.colorScheme.secondaryContainer,
        shape = MaterialTheme.shapes.medium,
        modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 12.dp),
    ) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Outlined.Info, contentDescription = null)
            Spacer(Modifier.width(8.dp))
            Text(
                "The reply you were looking for was deleted or is no longer available.",
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}

@Composable
private fun RepliesHeader(state: ReviewUiState, onLogin: () -> Unit) {
    Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 20.dp, bottom = 4.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            "Replies (${state.thread.replyCount})",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
        )
        when {
            !state.isReplyable -> Text(
                "Private reviews can't be replied to.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            !state.isLoggedIn -> Card(Modifier.fillMaxWidth()) {
                Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Log in to join the conversation.", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                    Button(onClick = onLogin) { Text("Log in") }
                }
            }
        }
    }
}

@Composable
private fun ReplyItem(
    reply: Reply,
    highlighted: Boolean,
    canReply: Boolean,
    isExpanded: Boolean,
    onOpenProfile: (String) -> Unit,
    onReply: () -> Unit,
    onToggleChildren: () -> Unit,
    onDelete: () -> Unit,
) {
    val nested = !reply.isTopLevel
    Row(
        Modifier
            .fillMaxWidth()
            .height(IntrinsicSize.Min)
            .padding(start = if (nested) 40.dp else 16.dp, end = 16.dp, top = 4.dp, bottom = 4.dp)
            .clip(MaterialTheme.shapes.medium)
            .background(if (highlighted) MaterialTheme.colorScheme.primary.copy(alpha = 0.12f) else Color.Transparent),
    ) {
        // A thin rule ties replies to replies to their parent, like the web's left border
        if (nested) {
            Box(Modifier.width(2.dp).fillMaxHeight().background(MaterialTheme.colorScheme.outlineVariant))
        }
        Column(Modifier.weight(1f).padding(start = 8.dp, top = 8.dp, bottom = 4.dp)) {
            Row(Modifier.heightIn(min = 32.dp), verticalAlignment = Alignment.CenterVertically) {
                if (reply.isDeleted) {
                    Text(
                        "This reply was deleted",
                        style = MaterialTheme.typography.bodyMedium,
                        fontStyle = FontStyle.Italic,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f),
                    )
                } else {
                    val username = reply.username.orEmpty()
                    Row(
                        Modifier.clip(MaterialTheme.shapes.small).clickable { onOpenProfile(username) },
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        UserAvatar(username, reply.profilePictureUrl, size = 28.dp)
                        Spacer(Modifier.width(8.dp))
                        Text(username, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                    }
                    Spacer(Modifier.width(8.dp))
                    Text(
                        formatDate(reply.createdAt).orEmpty(),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f),
                    )
                }
                if (reply.isOwner) {
                    IconButton(onClick = onDelete, modifier = Modifier.size(32.dp)) {
                        Icon(
                            Icons.Outlined.Delete,
                            contentDescription = "Delete reply",
                            tint = MaterialTheme.colorScheme.error,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
            }
            if (!reply.isDeleted) {
                Text(reply.content, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 4.dp, end = 8.dp))
            }
            val hasChildren = reply.isTopLevel && reply.childCount > 0
            if (canReply || hasChildren) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (canReply) {
                        TextButton(onClick = onReply, contentPadding = CompactButtonPadding) { Text("Reply") }
                    }
                    if (hasChildren) {
                        TextButton(onClick = onToggleChildren, contentPadding = CompactButtonPadding) {
                            Text(if (isExpanded) "Hide replies" else "View ${pluralize(reply.childCount, "reply", "replies")}")
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ReplyBar(
    state: ReviewUiState,
    focusRequester: FocusRequester,
    onDraftChange: (String) -> Unit,
    onClearTarget: () -> Unit,
    onSend: () -> Unit,
) {
    Surface(tonalElevation = 3.dp, modifier = Modifier.imePadding()) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp)) {
            state.replyTarget?.let { target ->
                InputChip(
                    selected = false,
                    onClick = onClearTarget,
                    label = { Text("Replying to ${target.username ?: "a deleted reply"}") },
                    trailingIcon = { Icon(Icons.Filled.Close, contentDescription = "Cancel reply", Modifier.size(18.dp)) },
                )
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = state.draft,
                    onValueChange = onDraftChange,
                    placeholder = {
                        Text(state.replyTarget?.username?.let { "Reply to $it..." } ?: "Write a reply...")
                    },
                    maxLines = 5,
                    isError = state.draftError != null || state.postError != null,
                    supportingText = {
                        val error = state.postError ?: state.draftError
                        Row {
                            if (error != null) Text(error, modifier = Modifier.weight(1f)) else Spacer(Modifier.weight(1f))
                            Text("${state.draft.trim().length} / ${Validation.MAX_REPLY_LENGTH}")
                        }
                    },
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
                    modifier = Modifier.weight(1f).focusRequester(focusRequester),
                )
                if (state.isPosting) {
                    CircularProgressIndicator(Modifier.padding(12.dp).size(24.dp), strokeWidth = 2.dp)
                } else {
                    IconButton(onClick = onSend, enabled = state.canSend) {
                        Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "Send reply")
                    }
                }
            }
        }
    }
}

@Composable
private fun ReviewDialogs(state: ReviewUiState, viewModel: ReviewViewModel) {
    state.replyToDelete?.let {
        AlertDialog(
            onDismissRequest = { if (!state.isDeleting) viewModel.askDeleteReply(null) },
            title = { Text("Delete reply?") },
            text = { Text("Are you sure you want to delete this reply? This can't be undone.") },
            confirmButton = {
                TextButton(onClick = viewModel::deleteReply, enabled = !state.isDeleting) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(onClick = { viewModel.askDeleteReply(null) }, enabled = !state.isDeleting) { Text("Cancel") }
            },
        )
    }
    if (state.askDeleteReview) {
        val title = state.detail?.review?.mediaTitle.orEmpty()
        AlertDialog(
            onDismissRequest = { if (!state.isDeleting) viewModel.askDeleteReview(false) },
            title = { Text("Delete review?") },
            text = { Text("Your review of $title will be deleted with its replies. This can't be undone.") },
            confirmButton = {
                TextButton(onClick = viewModel::deleteReview, enabled = !state.isDeleting) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(onClick = { viewModel.askDeleteReview(false) }, enabled = !state.isDeleting) { Text("Cancel") }
            },
        )
    }
}
