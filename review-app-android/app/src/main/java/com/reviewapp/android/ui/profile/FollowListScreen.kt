package com.reviewapp.android.ui.profile

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.PrimaryTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Tab
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.toRoute
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.UserRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.UserSummary
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.UserAvatar
import com.reviewapp.android.ui.navigation.FollowListRoute
import kotlinx.coroutines.async
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class FollowListUiState(
    val showFollowing: Boolean = false,
    val followers: List<UserSummary> = emptyList(),
    val following: List<UserSummary> = emptyList(),
    val isLoading: Boolean = true,
    val error: String? = null,
    val currentUserId: Int? = null,
    val isOwner: Boolean = false,
    val busyUserIds: Set<Int> = emptySet(),
    val followerToRemove: UserSummary? = null,
) {
    val users: List<UserSummary> get() = if (showFollowing) following else followers
}

/** The web's `FollowerListDialog` as a screen: both lists, follow/unfollow, and removing own followers. */
class FollowListViewModel(
    route: FollowListRoute,
    private val userRepository: UserRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    val username = route.username

    private val _state = MutableStateFlow(FollowListUiState(showFollowing = route.showFollowing))
    val state: StateFlow<FollowListUiState> = _state.asStateFlow()

    private val _messages = Channel<String>(Channel.BUFFERED)
    val messages = _messages.receiveAsFlow()

    private val _loginRequired = Channel<Unit>(Channel.CONFLATED)
    val loginRequired = _loginRequired.receiveAsFlow()

    init {
        // The "do I follow them" flags depend on who is logged in
        viewModelScope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user }
                .distinctUntilChanged { old, new -> old?.id == new?.id }
                .collect { user ->
                    _state.update { it.copy(currentUserId = user?.id, isOwner = user?.username == username) }
                    load()
                }
        }
    }

    fun load() {
        _state.update { it.copy(isLoading = true, error = null) }
        viewModelScope.launch {
            val followers = async { userRepository.followers(username) }
            val following = async { userRepository.following(username) }
            val f1 = followers.await()
            val f2 = following.await()
            _state.update {
                if (f1 is ApiResult.Success && f2 is ApiResult.Success) {
                    it.copy(followers = f1.data, following = f2.data, isLoading = false)
                } else {
                    it.copy(isLoading = false, error = (f1 as? ApiResult.Error ?: f2 as ApiResult.Error).message)
                }
            }
        }
    }

    fun selectTab(showFollowing: Boolean) = _state.update { it.copy(showFollowing = showFollowing) }

    /** Optimistic, put back on failure; the user stays in the list either way (like the web). */
    fun toggleFollow(user: UserSummary) {
        val current = _state.value
        if (current.currentUserId == null) {
            _loginRequired.trySend(Unit)
            return
        }
        if (user.id in current.busyUserIds) return
        val follow = !user.isFollowed
        setFollowed(user.id, follow, busy = true)
        viewModelScope.launch {
            val result = userRepository.setFollowing(user.id, follow)
            setFollowed(user.id, if (result is ApiResult.Success) follow else !follow, busy = false)
            if (result is ApiResult.Error) _messages.send(result.message)
        }
    }

    private fun setFollowed(userId: Int, followed: Boolean, busy: Boolean) = _state.update { state ->
        fun List<UserSummary>.mark() = map { if (it.id == userId) it.copy(isFollowed = followed) else it }
        state.copy(
            followers = state.followers.mark(),
            following = state.following.mark(),
            busyUserIds = if (busy) state.busyUserIds + userId else state.busyUserIds - userId,
        )
    }

    fun askRemoveFollower(user: UserSummary?) = _state.update { it.copy(followerToRemove = user) }

    fun removeFollower() {
        val user = _state.value.followerToRemove ?: return
        _state.update { it.copy(followerToRemove = null, busyUserIds = it.busyUserIds + user.id) }
        viewModelScope.launch {
            val result = userRepository.removeFollower(user.id)
            _state.update { state ->
                state.copy(
                    followers = if (result is ApiResult.Success) state.followers.filterNot { it.id == user.id } else state.followers,
                    busyUserIds = state.busyUserIds - user.id,
                )
            }
            if (result is ApiResult.Error) _messages.send(result.message)
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                FollowListViewModel(createSavedStateHandle().toRoute<FollowListRoute>(), c.userRepository, c.sessionManager)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FollowListScreen(
    onBack: () -> Unit,
    onOpenProfile: (String) -> Unit,
    onLoginRequired: () -> Unit,
    viewModel: FollowListViewModel = viewModel(factory = FollowListViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val currentOnLoginRequired by rememberUpdatedState(onLoginRequired)

    LaunchedEffect(viewModel) {
        launch { viewModel.messages.collect { snackbarHostState.showSnackbar(it) } }
        viewModel.loginRequired.collect { currentOnLoginRequired() }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text(viewModel.username) }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            PrimaryTabRow(selectedTabIndex = if (state.showFollowing) 1 else 0) {
                Tab(
                    selected = !state.showFollowing,
                    onClick = { viewModel.selectTab(false) },
                    text = { Text("Followers (${state.followers.size})") },
                    unselectedContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Tab(
                    selected = state.showFollowing,
                    onClick = { viewModel.selectTab(true) },
                    text = { Text("Following (${state.following.size})") },
                    unselectedContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            when {
                state.isLoading -> LoadingState()
                state.error != null -> ErrorState(state.error!!, viewModel::load, Modifier.fillMaxWidth())
                state.users.isEmpty() -> EmptyState(if (state.showFollowing) "Not following anyone yet." else "No followers yet.")
                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(state.users, key = { it.id }) { user ->
                        UserRow(
                            user = user,
                            isSelf = user.id == state.currentUserId,
                            canRemove = state.isOwner && !state.showFollowing,
                            isBusy = user.id in state.busyUserIds,
                            onOpen = { onOpenProfile(user.username) },
                            onToggleFollow = { viewModel.toggleFollow(user) },
                            onRemove = { viewModel.askRemoveFollower(user) },
                        )
                    }
                }
            }
        }
    }

    state.followerToRemove?.let { user ->
        AlertDialog(
            onDismissRequest = { viewModel.askRemoveFollower(null) },
            title = { Text("Remove follower?") },
            text = { Text("${user.username} won't follow you anymore. They can follow you again later.") },
            confirmButton = {
                TextButton(onClick = viewModel::removeFollower) { Text("Remove", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { viewModel.askRemoveFollower(null) }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun UserRow(
    user: UserSummary,
    isSelf: Boolean,
    canRemove: Boolean,
    isBusy: Boolean,
    onOpen: () -> Unit,
    onToggleFollow: () -> Unit,
    onRemove: () -> Unit,
) {
    ListItem(
        headlineContent = { Text(user.username, fontWeight = FontWeight.SemiBold) },
        leadingContent = { UserAvatar(user.username, user.profilePictureUrl) },
        trailingContent = if (isSelf) null else {
            {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (canRemove) {
                        OutlinedButton(
                            onClick = onRemove,
                            enabled = !isBusy,
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                        ) { Text("Remove") }
                    }
                    if (user.isFollowed) {
                        OutlinedButton(onClick = onToggleFollow, enabled = !isBusy) { Text("Unfollow") }
                    } else {
                        Button(onClick = onToggleFollow, enabled = !isBusy) { Text("Follow") }
                    }
                }
            }
        },
        modifier = Modifier.clickable(onClick = onOpen),
    )
}
