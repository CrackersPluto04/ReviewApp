package com.reviewapp.android.ui.collection

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalIconButton
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
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.CollectionItem
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.Poster
import com.reviewapp.android.ui.components.icon
import com.reviewapp.android.util.formatDate
import com.reviewapp.android.util.pluralize
import sh.calvin.reorderable.ReorderableCollectionItemScope
import sh.calvin.reorderable.ReorderableItem
import sh.calvin.reorderable.rememberReorderableLazyGridState

/** A collection's media in a grid. The owner can remove media and long-press to drag them into a new order. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CollectionScreen(
    onBack: () -> Unit,
    onOpenMedia: (Media) -> Unit,
    viewModel: CollectionViewModel = viewModel(factory = CollectionViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val currentOnBack by rememberUpdatedState(onBack)

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is CollectionEvent.Message -> snackbarHostState.showSnackbar(event.text)
                CollectionEvent.Deleted -> currentOnBack()
            }
        }
    }

    val collection = state.collection
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(collection?.name ?: "Collection", maxLines = 1, overflow = TextOverflow.Ellipsis) },
                navigationIcon = { BackButton(onBack) },
                actions = {
                    if (state.canEdit) {
                        IconButton(onClick = { viewModel.showEditDialog(true) }) {
                            Icon(Icons.Outlined.Edit, contentDescription = "Edit collection")
                        }
                        IconButton(onClick = { viewModel.askDelete(true) }) {
                            Icon(Icons.Outlined.Delete, contentDescription = "Delete collection")
                        }
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            collection == null && state.isLoading -> Box(modifier) { LoadingState() }
            collection == null -> Box(modifier) {
                if (state.notFound) {
                    EmptyState("This collection doesn't exist or is private.")
                } else {
                    ErrorState(state.error ?: "Could not load this collection.", { viewModel.load() }, Modifier.fillMaxWidth())
                }
            }
            else -> CollectionGrid(collection, state.items, viewModel, onOpenMedia, modifier)
        }
    }

    CollectionDialogs(state, viewModel)
}

@Composable
private fun CollectionGrid(
    collection: MediaCollection,
    items: List<CollectionItem>,
    viewModel: CollectionViewModel,
    onOpenMedia: (Media) -> Unit,
    modifier: Modifier,
) {
    val haptics = LocalHapticFeedback.current
    val gridState = rememberLazyGridState()
    val reorderState = rememberReorderableLazyGridState(gridState) { from, to ->
        viewModel.move(from.key, to.key)
        haptics.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
    }

    LazyVerticalGrid(
        columns = GridCells.Fixed(2),
        state = gridState,
        contentPadding = PaddingValues(16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = modifier,
    ) {
        item(key = "info", span = { GridItemSpan(maxLineSpan) }) {
            CollectionInfo(collection, showReorderHint = collection.isOwner && items.size > 1)
        }
        if (items.isEmpty()) {
            item(key = "empty", span = { GridItemSpan(maxLineSpan) }) {
                EmptyState(
                    if (collection.isOwner) "This collection is empty. Use the add-to-collection button on any media to add it here."
                    else "This collection is empty.",
                )
            }
        }
        items(items, key = { it.dbMediaId }) { item ->
            ReorderableItem(reorderState, key = item.dbMediaId, enabled = collection.isOwner) { isDragging ->
                CollectionMediaTile(
                    item = item,
                    isOwner = collection.isOwner,
                    isDragging = isDragging,
                    onOpen = { onOpenMedia(item.media) },
                    onRemove = { viewModel.askRemove(item) },
                    onDragStarted = {
                        haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                        viewModel.startDrag(item.dbMediaId)
                    },
                    onDragStopped = { viewModel.endDrag(item.dbMediaId) },
                )
            }
        }
    }
}

@Composable
private fun CollectionInfo(collection: MediaCollection, showReorderHint: Boolean) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Card(Modifier.fillMaxWidth()) {
            Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                if (collection.isDefault) {
                    Icon(Icons.Filled.Favorite, contentDescription = null, tint = FavouriteRed, modifier = Modifier.size(18.dp))
                } else {
                    Icon(collection.visibility.icon, contentDescription = null, modifier = Modifier.size(18.dp))
                }
                Spacer(Modifier.width(8.dp))
                Text(
                    listOfNotNull(
                        collection.visibility.label,
                        formatDate(collection.createdAt)?.let { "Created $it" },
                        pluralize(collection.mediaCount, "media", "media"),
                    ).joinToString(" • "),
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        }
        if (showReorderHint) {
            Text(
                "Long-press and drag a media to reorder.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun ReorderableCollectionItemScope.CollectionMediaTile(
    item: CollectionItem,
    isOwner: Boolean,
    isDragging: Boolean,
    onOpen: () -> Unit,
    onRemove: () -> Unit,
    onDragStarted: () -> Unit,
    onDragStopped: () -> Unit,
) {
    val media = item.media
    val interactionSource = remember { MutableInteractionSource() }
    val elevation by animateDpAsState(if (isDragging) 8.dp else 1.dp, label = "dragElevation")
    Card(
        onClick = onOpen,
        interactionSource = interactionSource,
        elevation = CardDefaults.cardElevation(defaultElevation = elevation),
        // A tap opens the media, a long press starts dragging
        modifier = if (isOwner) {
            Modifier.longPressDraggableHandle(
                onDragStarted = { onDragStarted() },
                onDragStopped = onDragStopped,
                interactionSource = interactionSource,
            )
        } else {
            Modifier
        },
    ) {
        Box {
            Poster(
                media.posterUrl,
                media.mediaType,
                Modifier.fillMaxWidth().aspectRatio(2f / 3f),
                cornerRadius = 0.dp,
            )
            if (isOwner) {
                FilledTonalIconButton(
                    onClick = onRemove,
                    modifier = Modifier.align(Alignment.TopEnd).padding(4.dp).size(32.dp),
                ) {
                    Icon(Icons.Filled.Close, contentDescription = "Remove from collection", modifier = Modifier.size(18.dp))
                }
            }
        }
        Column(Modifier.padding(8.dp)) {
            Text(
                media.title,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                listOfNotNull(media.mediaType.label, formatDate(media.releaseDate)).joinToString(" · "),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun CollectionDialogs(state: CollectionUiState, viewModel: CollectionViewModel) {
    val collection = state.collection ?: return
    if (state.showEditDialog) {
        CollectionFormDialog(
            title = "Edit collection",
            confirmLabel = "Save",
            isSaving = state.isSaving,
            error = state.editError,
            onConfirm = viewModel::save,
            onDismiss = { viewModel.showEditDialog(false) },
            initialName = collection.name,
            initialVisibility = collection.visibility,
        )
    }
    if (state.askDelete) {
        AlertDialog(
            onDismissRequest = { if (!state.isDeleting) viewModel.askDelete(false) },
            title = { Text("Delete collection?") },
            text = { Text("\"${collection.name}\" will be deleted. The media in it aren't affected. This can't be undone.") },
            confirmButton = {
                TextButton(onClick = viewModel::delete, enabled = !state.isDeleting) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = { TextButton(onClick = { viewModel.askDelete(false) }, enabled = !state.isDeleting) { Text("Cancel") } },
        )
    }
    state.itemToRemove?.let { item ->
        AlertDialog(
            onDismissRequest = { viewModel.askRemove(null) },
            title = { Text("Remove from collection?") },
            text = { Text("Remove ${item.media.title} from \"${collection.name}\"?") },
            confirmButton = {
                TextButton(onClick = viewModel::removeItem) { Text("Remove", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { viewModel.askRemove(null) }) { Text("Cancel") } },
        )
    }
}
