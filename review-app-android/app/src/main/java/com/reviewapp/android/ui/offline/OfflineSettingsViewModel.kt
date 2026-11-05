package com.reviewapp.android.ui.offline

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.local.offline.DatabaseManager
import com.reviewapp.android.data.sync.OfflineGateway
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.FeatureKeys
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class OfflineSettingsUiState(
    val isLoggedIn: Boolean = false,
    /** The user has the `offline-mode` entitlement (else the upsell). */
    val entitled: Boolean = false,
    val enabled: Boolean = false,
    val status: OfflineStatus = OfflineStatus(),
    /** Bytes of the offline copy on this phone. */
    val storageBytes: Long = 0,
    /** The "turn off?" confirmation is open. */
    val confirmingDisable: Boolean = false,
)

@OptIn(ExperimentalCoroutinesApi::class)
class OfflineSettingsViewModel(
    private val sessionManager: SessionManager,
    private val settings: SettingsStore,
    private val gateway: OfflineGateway,
    private val databases: DatabaseManager,
) : ViewModel() {

    private val confirming = MutableStateFlow(false)

    val state: StateFlow<OfflineSettingsUiState> = sessionManager.state
        .map { (it as? AuthState.LoggedIn)?.user }
        .flatMapLatest { user ->
            if (user == null) return@flatMapLatest flowOf(OfflineSettingsUiState())
            combine(settings.offlineEnabled(user.id), gateway.status, confirming) { enabled, status, confirmingDisable ->
                OfflineSettingsUiState(
                    isLoggedIn = true,
                    entitled = user.hasFeature(FeatureKeys.OFFLINE_MODE),
                    enabled = enabled,
                    status = status,
                    // Changes whenever the status does (a sync, a change), cheap file sizes
                    storageBytes = withContext(Dispatchers.IO) { databases.sizeBytes(user.id) },
                    confirmingDisable = confirmingDisable,
                )
            }
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), OfflineSettingsUiState())

    /** The switch: turning on is immediate, turning off asks first (the offline copy goes away). */
    fun onToggle(enabled: Boolean) {
        if (enabled) setEnabled(true) else confirming.value = true
    }

    fun confirmDisable() {
        confirming.value = false
        setEnabled(false)
    }

    fun dismissDisable() = confirming.update { false }

    private fun setEnabled(enabled: Boolean) {
        val userId = sessionManager.currentUser?.id ?: return
        viewModelScope.launch { settings.setOfflineEnabled(userId, enabled) }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                OfflineSettingsViewModel(c.sessionManager, c.settingsStore, c.offlineGateway, c.databaseManager)
            }
        }
    }
}
