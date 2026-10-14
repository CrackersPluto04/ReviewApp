package com.reviewapp.android.ui.collection

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.PlaylistAdd
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.data.repository.MyCollections
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.icon
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** The media whose add-to-collection sheet is open, and the "new collection" dialog in it. */
data class CollectionSheet(
    val media: Media,
    val showCreateDialog: Boolean = false,
    val isCreating: Boolean = false,
    val createError: String? = null,
)

data class CollectionActionsState(
    val isLoggedIn: Boolean = false,
    val mine: MyCollections = MyCollections(),
    val sheet: CollectionSheet? = null,
) {
    fun isFavourite(media: Media): Boolean = mine.isFavourite(media.key)
}

/**
 * Favourite and add-to-collection actions of the media cards. One instance for the whole app
 * (activity scoped), handed to the cards through [LocalCollectionActions] so every list gets
 * the buttons without passing callbacks through each screen.
 */
class CollectionActionsViewModel(
    private val collectionRepository: CollectionRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    private val sheet = MutableStateFlow<CollectionSheet?>(null)

    val state: StateFlow<CollectionActionsState> = combine(
        sessionManager.state,
        collectionRepository.mine,
        sheet,
    ) { auth, mine, sheet ->
        CollectionActionsState(isLoggedIn = auth is AuthState.LoggedIn, mine = mine, sheet = sheet)
    }.stateIn(viewModelScope, SharingStarted.Eagerly, CollectionActionsState())

    private val _messages = Channel<String>(Channel.BUFFERED)
    val messages = _messages.receiveAsFlow()

    fun toggleFavourite(media: Media) {
        viewModelScope.launch {
            val result = collectionRepository.toggleFavourite(media)
            if (result is ApiResult.Error) _messages.send(result.message)
        }
    }

    fun openSheet(media: Media) {
        collectionRepository.refreshIfNeeded()
        sheet.value = CollectionSheet(media)
    }

    fun closeSheet() {
        sheet.value = null
    }

    fun retryLoad() = collectionRepository.refresh()

    fun toggleInCollection(collection: MediaCollection) {
        val media = sheet.value?.media ?: return
        val add = !state.value.mine.contains(media.key, collection.id)
        viewModelScope.launch {
            val result = collectionRepository.setInCollection(media, collection.id, add)
            if (result is ApiResult.Error) _messages.send(result.message)
        }
    }

    fun showCreateDialog(show: Boolean) {
        sheet.update { it?.copy(showCreateDialog = show, createError = null) }
    }

    /** Creates a collection from the sheet and puts the sheet's media in it. */
    fun createAndAdd(name: String, visibility: Visibility) {
        val media = sheet.value?.media ?: return
        sheet.update { it?.copy(isCreating = true, createError = null) }
        viewModelScope.launch {
            when (val created = collectionRepository.create(name, visibility)) {
                is ApiResult.Error -> sheet.update { it?.copy(isCreating = false, createError = created.message) }
                is ApiResult.Success -> {
                    sheet.update { it?.copy(isCreating = false, showCreateDialog = false) }
                    val added = collectionRepository.setInCollection(media, created.data.id, add = true)
                    if (added is ApiResult.Error) _messages.send(added.message)
                }
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                CollectionActionsViewModel(c.collectionRepository, c.sessionManager)
            }
        }
    }
}

val LocalCollectionActions = staticCompositionLocalOf<CollectionActionsViewModel?> { null }

/** Provides [LocalCollectionActions] to [content] and shows the add-to-collection sheet and errors. */
@Composable
fun CollectionActionsHost(
    snackbarHostState: SnackbarHostState,
    viewModel: CollectionActionsViewModel = viewModel(factory = CollectionActionsViewModel.Factory),
    content: @Composable () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    LaunchedEffect(viewModel) {
        viewModel.messages.collect { snackbarHostState.showSnackbar(it) }
    }

    CompositionLocalProvider(LocalCollectionActions provides viewModel) {
        content()
    }

    val sheet = state.sheet
    if (sheet != null) {
        // Logging out while the sheet is open closes it
        if (!state.isLoggedIn) {
            LaunchedEffect(Unit) { viewModel.closeSheet() }
        } else {
            AddToCollectionSheet(state.mine, sheet, viewModel)
        }
        if (sheet.showCreateDialog) {
            CollectionFormDialog(
                title = "New collection",
                confirmLabel = "Create and add",
                isSaving = sheet.isCreating,
                error = sheet.createError,
                onConfirm = viewModel::createAndAdd,
                onDismiss = { viewModel.showCreateDialog(false) },
            )
        }
    }
}

/** Heart (Favourites toggle) and add-to-collection buttons, only for logged-in users. */
@Composable
fun MediaCollectionButtons(media: Media, modifier: Modifier = Modifier) {
    val actions = LocalCollectionActions.current ?: return
    val state by actions.state.collectAsStateWithLifecycle()
    if (!state.isLoggedIn) return

    val isFavourite = state.isFavourite(media)
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = { actions.openSheet(media) }) {
            Icon(Icons.AutoMirrored.Filled.PlaylistAdd, contentDescription = "Add to collection")
        }
        IconButton(onClick = { actions.toggleFavourite(media) }) {
            Icon(
                if (isFavourite) Icons.Filled.Favorite else Icons.Filled.FavoriteBorder,
                contentDescription = if (isFavourite) "Remove from Favourites" else "Add to Favourites",
                tint = if (isFavourite) FavouriteRed else MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

val FavouriteRed = Color(0xFFE53935)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AddToCollectionSheet(mine: MyCollections, sheet: CollectionSheet, viewModel: CollectionActionsViewModel) {
    ModalBottomSheet(
        onDismissRequest = viewModel::closeSheet,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(Modifier.navigationBarsPadding()) {
            Column(Modifier.padding(horizontal = 24.dp)) {
                Text("Save to collection", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
                Text(
                    sheet.media.title,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            when {
                !mine.isLoaded && mine.error != null -> ErrorState(mine.error, viewModel::retryLoad, Modifier.fillMaxWidth())
                !mine.isLoaded -> LoadingState()
                else -> LazyColumn(Modifier.padding(top = 8.dp)) {
                    items(mine.collections, key = { it.id }) { collection ->
                        val checked = mine.contains(sheet.media.key, collection.id)
                        ListItem(
                            headlineContent = { Text(collection.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                            leadingContent = {
                                Checkbox(checked = checked, onCheckedChange = { viewModel.toggleInCollection(collection) })
                            },
                            trailingContent = {
                                Icon(
                                    if (collection.isDefault) Icons.Filled.Favorite else collection.visibility.icon,
                                    contentDescription = collection.visibility.label,
                                    tint = if (collection.isDefault) FavouriteRed else MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            },
                            colors = ListItemDefaults.colors(containerColor = Color.Transparent),
                            modifier = Modifier.clickable { viewModel.toggleInCollection(collection) },
                        )
                    }
                    item(key = "new") {
                        ListItem(
                            headlineContent = { Text("New collection") },
                            leadingContent = { Icon(Icons.Filled.Add, contentDescription = null) },
                            colors = ListItemDefaults.colors(containerColor = Color.Transparent),
                            modifier = Modifier.clickable { viewModel.showCreateDialog(true) },
                        )
                    }
                }
            }
        }
    }
}
