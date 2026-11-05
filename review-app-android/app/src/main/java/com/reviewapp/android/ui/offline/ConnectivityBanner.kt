package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.OfflineMode
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.util.pluralize

/**
 * The connectivity strip's content: [problem] is tinted as an error (the backend is down, editing is locked, changes
 * need the user), [disconnected] shows the "cloud off" icon (else the sync one), [opensChanges] makes it a way to
 * the pending changes.
 */
data class ConnectivityBanner(
    val text: String,
    val problem: Boolean = false,
    val disconnected: Boolean = true,
    val opensChanges: Boolean = false,
)

/** What the sync is doing, for the strip. */
data class SyncActivity(val running: Boolean = false, val paused: Boolean = false)

/** What the strip says, or null when it is hidden (online, nothing waiting). Pure, so the texts are unit-tested. */
fun connectivityBanner(connectivity: Connectivity, status: OfflineStatus, sync: SyncActivity = SyncActivity()): ConnectivityBanner? {
    val hasChanges = status.pendingChanges > 0
    val waiting = status.toSend.takeIf { it > 0 }?.let { "${pluralize(it, "change")} waiting to sync" }
    val attention = status.issues.takeIf { it > 0 }?.let { if (it == 1) "1 change needs your attention" else "$it changes need your attention" }
    val mode = status.mode
    return when (connectivity) {
        Connectivity.Online -> when {
            sync.paused && status.toSend > 0 -> ConnectivityBanner("Sync paused, it continues shortly", disconnected = false, opensChanges = true)
            sync.running && status.toSend > 0 -> ConnectivityBanner("Syncing ${pluralize(status.toSend, "change")}…", disconnected = false, opensChanges = true)
            waiting != null -> ConnectivityBanner(waiting.replaceFirstChar(Char::uppercase), disconnected = false, opensChanges = true)
            attention != null -> ConnectivityBanner(attention.replaceFirstChar(Char::uppercase), problem = true, disconnected = false, opensChanges = true)
            else -> null
        }
        Connectivity.ServerUnreachable -> ConnectivityBanner(
            listOfNotNull("Can't reach ReviewApp right now", waiting).joinToString(" · "),
            problem = true,
            opensChanges = hasChanges,
        )
        Connectivity.Offline -> when (mode) {
            OfflineMode.Inactive -> ConnectivityBanner("You're offline")
            is OfflineMode.ReadOnly -> ConnectivityBanner("Offline · reconnect to keep editing", problem = true, opensChanges = hasChanges)
            is OfflineMode.Preparing -> ConnectivityBanner("Offline · your offline copy isn't ready yet")
            is OfflineMode.Active, is OfflineMode.Draining ->
                ConnectivityBanner("Offline · " + (waiting ?: "showing what's saved on this phone"), opensChanges = hasChanges)
        }
    }
}
