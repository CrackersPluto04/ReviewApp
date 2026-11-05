package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.OfflineMode
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.util.pluralize

/**
 * The connectivity strip's content: [problem] is tinted as an error (the backend is down, editing is locked),
 * [disconnected] shows the "cloud off" icon (else the sync one).
 */
data class ConnectivityBanner(val text: String, val problem: Boolean = false, val disconnected: Boolean = true)

/** What the strip says, or null when it is hidden (online, nothing waiting). Pure, so the texts are unit-tested. */
fun connectivityBanner(connectivity: Connectivity, status: OfflineStatus): ConnectivityBanner? {
    val waiting = status.pendingChanges.takeIf { it > 0 }?.let { "${pluralize(it, "change")} waiting to sync" }
    val mode = status.mode
    return when (connectivity) {
        Connectivity.Online -> waiting?.let { ConnectivityBanner(it.replaceFirstChar(Char::uppercase), disconnected = false) }
        Connectivity.ServerUnreachable -> ConnectivityBanner(
            listOfNotNull("Can't reach ReviewApp right now", waiting).joinToString(" · "),
            problem = true,
        )
        Connectivity.Offline -> when (mode) {
            OfflineMode.Inactive -> ConnectivityBanner("You're offline")
            is OfflineMode.ReadOnly -> ConnectivityBanner("Offline · reconnect to keep editing", problem = true)
            is OfflineMode.Preparing -> ConnectivityBanner("Offline · your offline copy isn't ready yet")
            is OfflineMode.Active, is OfflineMode.Draining ->
                ConnectivityBanner("Offline · " + (waiting ?: "showing what's saved on this phone"))
        }
    }
}
