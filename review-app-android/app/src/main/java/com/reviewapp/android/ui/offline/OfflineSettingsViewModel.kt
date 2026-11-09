package com.reviewapp.android.ui.offline

import android.app.Application
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import coil.imageLoader
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.CacheBudget
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.local.offline.DatabaseManager
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.ConnectivityMonitor
import com.reviewapp.android.data.sync.LibraryDownloadState
import com.reviewapp.android.data.sync.LibraryDownloadWorker
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.OfflineGateway
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.data.sync.SyncEngine
import com.reviewapp.android.data.sync.SyncOutcome
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.FeatureKeys
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** The open confirmation or choice of the offline settings. */
enum class OfflineDialog { Disable, ClearCache, Budget, DiscardOtherAccounts }

data class OfflineSettingsUiState(
    val isLoggedIn: Boolean = false,
    /** The user has the `offline-mode` entitlement (else the upsell). */
    val entitled: Boolean = false,
    val enabled: Boolean = false,
    val status: OfflineStatus = OfflineStatus(),
    val isOnline: Boolean = true,
    val isSyncing: Boolean = false,
    val download: LibraryDownloadState = LibraryDownloadState.Idle,
    val wifiOnly: Boolean = true,
    val budget: CacheBudget = CacheBudget.Default,
    /** Bytes of the offline copy on this phone (database and posters). */
    val storageBytes: Long = 0,
    val dialog: OfflineDialog? = null,
    /** Another account's unsent changes are on this phone (a neutral notice with Discard). */
    val otherAccountChanges: Boolean = false,
)

/** The pieces of the screen that change together, combined in two steps (combine takes five flows). */
private data class Device(val isOnline: Boolean, val isSyncing: Boolean, val download: LibraryDownloadState, val wifiOnly: Boolean, val budget: CacheBudget)

@OptIn(ExperimentalCoroutinesApi::class)
class OfflineSettingsViewModel(
    private val application: Application,
    private val sessionManager: SessionManager,
    private val settings: SettingsStore,
    private val gateway: OfflineGateway,
    private val databases: DatabaseManager,
    private val syncEngine: SyncEngine,
    private val local: LocalLibrary,
    connectivity: ConnectivityMonitor,
) : ViewModel() {

    private val dialog = MutableStateFlow<OfflineDialog?>(null)

    private val _messages = Channel<String>(Channel.BUFFERED)
    val messages = _messages.receiveAsFlow()

    private val device = combine(
        connectivity.connectivity.map { it == Connectivity.Online },
        syncEngine.running,
        LibraryDownloadWorker.state(application),
        settings.offlineDownloadWifiOnly,
        settings.cacheBudget,
    ) { online, syncing, download, wifiOnly, budget -> Device(online, syncing, download, wifiOnly, budget) }

    val state: StateFlow<OfflineSettingsUiState> = sessionManager.state
        .map { (it as? AuthState.LoggedIn)?.user }
        .flatMapLatest { user ->
            if (user == null) return@flatMapLatest flowOf(OfflineSettingsUiState())
            combine(settings.offlineEnabled(user.id), gateway.status, device, dialog, gateway.otherAccountChanges) { enabled, status, device, dialog, others ->
                OfflineSettingsUiState(
                    isLoggedIn = true,
                    entitled = user.hasFeature(FeatureKeys.OFFLINE_MODE),
                    enabled = enabled,
                    status = status,
                    isOnline = device.isOnline,
                    isSyncing = device.isSyncing,
                    download = device.download,
                    wifiOnly = device.wifiOnly,
                    budget = device.budget,
                    // Changes whenever the status does (a sync, a change); cheap file sizes
                    storageBytes = withContext(Dispatchers.IO) { databases.sizeBytes(user.id) },
                    dialog = dialog,
                    otherAccountChanges = others,
                )
            }
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), OfflineSettingsUiState())

    /** The switch: turning on is immediate, turning off asks first (the offline copy goes away). */
    fun onToggle(enabled: Boolean) {
        if (enabled) setEnabled(true) else dialog.value = OfflineDialog.Disable
    }

    fun confirmDisable() {
        dialog.value = null
        LibraryDownloadWorker.cancel(application)
        setEnabled(false)
    }

    fun openDialog(which: OfflineDialog) = dialog.update { which }

    fun dismissDialog() = dialog.update { null }

    private fun setEnabled(enabled: Boolean) {
        val userId = sessionManager.currentUser?.id ?: return
        viewModelScope.launch { settings.setOfflineEnabled(userId, enabled) }
    }

    fun syncNow() {
        viewModelScope.launch {
            when (val outcome = syncEngine.syncNow()) {
                SyncOutcome.Done -> _messages.send("Your offline copy is up to date.")
                SyncOutcome.Skipped -> _messages.send("Syncing needs a connection.")
                is SyncOutcome.Failed -> _messages.send(outcome.message)
            }
        }
    }

    fun startDownload() = LibraryDownloadWorker.start(application, state.value.wifiOnly)

    fun cancelDownload() = LibraryDownloadWorker.cancel(application)

    fun setWifiOnly(wifiOnly: Boolean) {
        viewModelScope.launch {
            settings.setOfflineDownloadWifiOnly(wifiOnly)
            // A waiting download takes the new network rule
            if (state.value.download !is LibraryDownloadState.Idle) LibraryDownloadWorker.start(application, wifiOnly)
        }
    }

    fun setBudget(budget: CacheBudget) {
        dialog.value = null
        viewModelScope.launch { settings.setCacheBudget(budget) }
    }

    fun clearCache() {
        dialog.value = null
        viewModelScope.launch {
            local.clearCache()
            application.imageLoader.memoryCache?.clear()
            withContext(Dispatchers.IO) { application.imageLoader.diskCache?.clear() }
            _messages.send("Cached data cleared. Your own content and waiting changes are kept.")
        }
    }

    fun discardOtherAccounts() {
        dialog.value = null
        viewModelScope.launch {
            gateway.discardOtherAccounts()
            _messages.send("The other account's changes were discarded.")
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                val app = this[ViewModelProvider.AndroidViewModelFactory.APPLICATION_KEY] as Application
                OfflineSettingsViewModel(
                    app, c.sessionManager, c.settingsStore, c.offlineGateway, c.databaseManager, c.syncEngine,
                    c.localLibrary, c.connectivityMonitor,
                )
            }
        }
    }
}
