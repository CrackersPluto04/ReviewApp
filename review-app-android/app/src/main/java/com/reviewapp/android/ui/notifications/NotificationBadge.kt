package com.reviewapp.android.ui.notifications

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.repository.NotificationRepository
import com.reviewapp.android.di.appContainer
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn

/** Activity-scoped: the unseen count for the bell and the Home tab, null while logged out (no bell). */
class NotificationBadgeViewModel(
    private val repository: NotificationRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    val unseenCount: StateFlow<Int?> = combine(sessionManager.state, repository.unseenCount) { auth, count ->
        if (auth is AuthState.LoggedIn) count else null
    }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    suspend fun refresh() = repository.refreshUnseen()

    companion object {
        /** Like the web's `NotificationContext`, which polls only while the tab is visible. */
        const val POLL_INTERVAL_MS = 45_000L

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                NotificationBadgeViewModel(c.notificationRepository, c.sessionManager)
            }
        }
    }
}

/** Polls the unseen count while logged in and the app is visible; coming back to it refreshes at once. */
@Composable
fun NotificationPollingEffect(viewModel: NotificationBadgeViewModel, isLoggedIn: Boolean) {
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(isLoggedIn, lifecycleOwner) {
        if (!isLoggedIn) return@LaunchedEffect
        lifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refresh()
                delay(NotificationBadgeViewModel.POLL_INTERVAL_MS)
            }
        }
    }
}

/** The top bar's bell with the unseen count. */
@Composable
fun NotificationBell(unseenCount: Int, onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        BadgedBox(
            badge = {
                if (unseenCount > 0) Badge { Text(if (unseenCount > 99) "99+" else unseenCount.toString()) }
            },
        ) {
            Icon(
                Icons.Outlined.Notifications,
                contentDescription = if (unseenCount > 0) "Notifications, $unseenCount unseen" else "Notifications",
            )
        }
    }
}
