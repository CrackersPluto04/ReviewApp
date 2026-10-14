package com.reviewapp.android.ui.notifications

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.EmojiEvents
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.DeleteSweep
import androidx.compose.material.icons.outlined.DoneAll
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.MarkEmailRead
import androidx.compose.material.icons.outlined.MarkEmailUnread
import androidx.compose.material.icons.outlined.PersonAdd
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import com.reviewapp.android.domain.model.NotificationType
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.DropdownFilterChip
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.FilterChipRow
import com.reviewapp.android.ui.components.InfiniteListEffect
import com.reviewapp.android.ui.components.ListFooter
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.UserAvatar
import com.reviewapp.android.util.Option
import com.reviewapp.android.util.formatDateTime

private val sortOptions = NotificationSort.entries.map { Option(it.name, it.label) }

/** The current user's notifications (the web's `NotificationsDialog`, full screen as in the wireframe). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NotificationsScreen(
    onBack: () -> Unit,
    onLogin: () -> Unit,
    onOpenTarget: (NotificationTarget) -> Unit,
    viewModel: NotificationsViewModel = viewModel(factory = NotificationsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val unseenCount by viewModel.unseenCount.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val listState = rememberLazyListState()
    val currentOnOpenTarget by rememberUpdatedState(onOpenTarget)

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is NotificationsEvent.Message -> snackbarHostState.showSnackbar(event.text)
                is NotificationsEvent.Open -> currentOnOpenTarget(event.target)
            }
        }
    }
    InfiniteListEffect(listState) { viewModel.loadMore() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(if (state.isLoggedIn) "Notifications (${state.list.totalCount})" else "Notifications")
                        if (state.isLoggedIn) {
                            Text(
                                if (unseenCount == 0) "You are all caught up" else "$unseenCount unseen",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                },
                navigationIcon = { BackButton(onBack) },
                actions = {
                    if (state.isLoggedIn) {
                        IconButton(onClick = viewModel::markAllSeen, enabled = unseenCount > 0 && !state.isBusy) {
                            Icon(Icons.Outlined.DoneAll, contentDescription = "Mark all as seen")
                        }
                        OverflowMenu(enabled = !state.isBusy, onDeleteSeen = { viewModel.askDeleteSeen(true) })
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        if (!state.isLoggedIn) {
            LoggedOut(onLogin, modifier)
            return@Scaffold
        }
        val list = state.list
        LazyColumn(
            state = listState,
            contentPadding = PaddingValues(bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            modifier = modifier,
        ) {
            item(key = "filters") {
                FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
                    FilterChip(selected = state.seenOn, onClick = viewModel::toggleSeenFilter, label = { Text("Seen") })
                    FilterChip(selected = state.unseenOn, onClick = viewModel::toggleUnseenFilter, label = { Text("Unseen") })
                    DropdownFilterChip(
                        label = "Sort by",
                        options = sortOptions,
                        selected = state.sort.name,
                        onSelect = { name -> viewModel.selectSort(NotificationSort.entries.firstOrNull { it.name == name } ?: NotificationSort.Newest) },
                        highlighted = state.sort != NotificationSort.Newest,
                    )
                }
            }
            when {
                !list.loaded && list.isLoading -> item { LoadingState() }
                list.items.isEmpty() && list.error != null -> item { ErrorState(list.error, viewModel::retry) }
                list.isEmpty -> item {
                    EmptyState(
                        when (state.status) {
                            NotificationStatus.All -> "You have no notifications yet."
                            NotificationStatus.Seen -> "No seen notifications."
                            NotificationStatus.Unseen -> "No unseen notifications."
                        },
                    )
                }
                else -> {
                    items(list.items, key = { it.id }) { notification ->
                        NotificationCard(
                            notification = notification,
                            hasTarget = viewModel.hasTarget(notification),
                            onToggleSeen = { viewModel.toggleSeen(notification) },
                            onDelete = { viewModel.delete(notification) },
                            onOpen = { viewModel.open(notification) },
                            modifier = Modifier.padding(horizontal = 16.dp).animateItem(),
                        )
                    }
                    item(key = "footer") { ListFooter(list.isLoading, list.error, viewModel::retry) }
                }
            }
        }
    }

    if (state.askDeleteSeen) {
        AlertDialog(
            onDismissRequest = { viewModel.askDeleteSeen(false) },
            title = { Text("Delete seen notifications?") },
            text = { Text("Delete every seen notification? This cannot be undone.") },
            confirmButton = {
                TextButton(onClick = viewModel::deleteSeen) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = { TextButton(onClick = { viewModel.askDeleteSeen(false) }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun OverflowMenu(enabled: Boolean, onDeleteSeen: () -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { expanded = true }) { Icon(Icons.Filled.MoreVert, contentDescription = "More options") }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = { Text("Delete all seen") },
                leadingIcon = { Icon(Icons.Outlined.DeleteSweep, contentDescription = null) },
                enabled = enabled,
                onClick = {
                    expanded = false
                    onDeleteSeen()
                },
            )
        }
    }
}

@Composable
private fun NotificationCard(
    notification: AppNotification,
    hasTarget: Boolean,
    onToggleSeen: () -> Unit,
    onDelete: () -> Unit,
    onOpen: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val unseen = !notification.isSeen
    val colors = MaterialTheme.colorScheme
    val containerColor = if (unseen) colors.primary.copy(alpha = 0.10f).compositeOver(colors.surfaceContainerHighest)
    else colors.surfaceContainerHighest
    val cardColors = CardDefaults.cardColors(containerColor = containerColor)
    val content: @Composable () -> Unit = {
        // The accent stripe needs the row's full height
        Row(Modifier.height(IntrinsicSize.Min)) {
            if (unseen) Box(Modifier.width(4.dp).fillMaxHeight().background(colors.primary))
            Column(Modifier.padding(start = 12.dp, top = 8.dp, bottom = 12.dp, end = 4.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    NotificationIcon(notification)
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(
                            notification.title,
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = if (unseen) FontWeight.Bold else FontWeight.Normal,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            listOfNotNull(notification.type.label, formatDateTime(notification.createdAt)).joinToString(" · "),
                            style = MaterialTheme.typography.bodySmall,
                            color = colors.onSurfaceVariant,
                        )
                    }
                    IconButton(onClick = onToggleSeen) {
                        Icon(
                            if (unseen) Icons.Outlined.MarkEmailRead else Icons.Outlined.MarkEmailUnread,
                            contentDescription = if (unseen) "Mark as seen" else "Mark as unseen",
                        )
                    }
                    IconButton(onClick = onDelete) { Icon(Icons.Outlined.Delete, contentDescription = "Delete") }
                }
                Row(Modifier.padding(top = 8.dp, end = 8.dp), verticalAlignment = Alignment.Bottom) {
                    Text(notification.message, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                    if (hasTarget) {
                        Spacer(Modifier.width(8.dp))
                        FilledTonalIconButton(onClick = onOpen) {
                            Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = "Take me there")
                        }
                    }
                }
            }
        }
    }
    if (hasTarget) {
        Card(onClick = onOpen, colors = cardColors, modifier = modifier.fillMaxWidth()) { content() }
    } else {
        Card(colors = cardColors, modifier = modifier.fillMaxWidth()) { content() }
    }
}

/** The actor's avatar, or an icon of the notification's type. */
@Composable
private fun NotificationIcon(notification: AppNotification) {
    val actor = notification.actorUsername
    if (actor != null) {
        UserAvatar(actor, notification.actorProfilePictureUrl)
        return
    }
    val icon = when (notification.type) {
        NotificationType.AchievementUnlocked -> Icons.Filled.EmojiEvents
        NotificationType.ReviewReply, NotificationType.ReplyReply -> Icons.Outlined.ChatBubbleOutline
        NotificationType.NewFollower -> Icons.Outlined.PersonAdd
        NotificationType.System -> Icons.Outlined.Info
    }
    Box(
        Modifier.size(40.dp).clip(CircleShape).background(MaterialTheme.colorScheme.secondaryContainer),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.onSecondaryContainer, modifier = Modifier.size(22.dp))
    }
}

@Composable
private fun LoggedOut(onLogin: () -> Unit, modifier: Modifier) {
    Column(
        modifier.padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("Log in to see your notifications.", style = MaterialTheme.typography.bodyLarge)
        Button(onClick = onLogin) { Text("Login / Register") }
    }
}
