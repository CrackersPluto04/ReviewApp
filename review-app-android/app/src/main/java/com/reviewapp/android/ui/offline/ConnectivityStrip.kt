package com.reviewapp.android.ui.offline

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CloudOff
import androidx.compose.material.icons.outlined.CloudSync
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import kotlinx.coroutines.flow.map
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.di.appContainer
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn

/**
 * Whether the backend can be reached, for screens whose features need a connection (camera recognition, the
 * subscription, account changes, replies, user search): they explain it instead of failing. Provided by the root.
 */
val LocalIsOnline = compositionLocalOf { true }

/** Activity scoped: the strip is part of the root, above the bottom bar. */
class ConnectivityViewModel(
    connectivity: StateFlow<Connectivity>,
    status: StateFlow<OfflineStatus>,
) : ViewModel() {

    val banner: StateFlow<ConnectivityBanner?> = combine(connectivity, status, ::connectivityBanner)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), connectivityBanner(connectivity.value, status.value))

    /** The backend can be reached (for [LocalIsOnline]). */
    val isOnline: StateFlow<Boolean> = connectivity.map { it == Connectivity.Online }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), connectivity.value == Connectivity.Online)

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                ConnectivityViewModel(c.connectivityMonitor.connectivity, c.offlineGateway.status)
            }
        }
    }
}

/**
 * A thin strip saying the app is offline, can't reach the backend, or has changes waiting; hidden otherwise.
 * [aboveNavigationBar]: the bottom bar is below it (else it keeps clear of the system navigation bar itself).
 */
@Composable
fun ConnectivityStrip(
    aboveNavigationBar: Boolean,
    viewModel: ConnectivityViewModel = viewModel(factory = ConnectivityViewModel.Factory),
) {
    val banner by viewModel.banner.collectAsStateWithLifecycle()
    // Keeps the last text while the strip animates away
    var shown by remember { mutableStateOf(banner) }
    if (banner != null) shown = banner
    AnimatedVisibility(visible = banner != null, enter = expandVertically(), exit = shrinkVertically()) {
        val current = shown ?: return@AnimatedVisibility
        val colors = MaterialTheme.colorScheme
        Surface(
            color = if (current.problem) colors.errorContainer else colors.inverseSurface,
            contentColor = if (current.problem) colors.onErrorContainer else colors.inverseOnSurface,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .then(if (aboveNavigationBar) Modifier else Modifier.windowInsetsPadding(WindowInsets.navigationBars))
                    .padding(horizontal = 16.dp, vertical = 6.dp)
                    .semantics { liveRegion = LiveRegionMode.Polite },
            ) {
                Icon(
                    if (current.disconnected) Icons.Outlined.CloudOff else Icons.Outlined.CloudSync,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp),
                )
                Text(
                    current.text,
                    style = MaterialTheme.typography.labelLarge,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(start = 8.dp),
                )
            }
        }
    }
}
