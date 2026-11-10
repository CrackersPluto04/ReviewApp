package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.sync.OfflineAccess
import com.reviewapp.android.data.sync.OfflineMode
import com.reviewapp.android.data.sync.OfflineStatus
import com.reviewapp.android.util.formatAgo
import com.reviewapp.android.util.formatTimeLeft
import com.reviewapp.android.util.pluralize

/** How the status card is coloured: plain, positive (ready, nothing waiting) or a warning. */
enum class StatusTone { Neutral, Good, Warning }

/** The status card of the offline settings. */
data class OfflineStatusView(val title: String, val message: String, val tone: StatusTone)

/** What the offline settings say about [status]; null when there is nothing to say (switched off, nothing left). */
fun offlineStatusView(status: OfflineStatus, nowMillis: Long): OfflineStatusView? {
    val waiting = status.pendingChanges.takeIf { it > 0 }?.let { pluralize(it, "change") }
    val lastSync = status.lastSyncedAt?.let { "Last synced ${formatAgo(it, nowMillis)}." } ?: "Not synced yet."
    // Only while the session wasn't confirmed by the backend in this run
    val graceNote = status.graceLeftMillis?.takeIf { it > 0 }?.let {
        " Connect within ${formatTimeLeft(it)} to keep editing offline."
    } ?: ""

    return when (status.mode) {
        OfflineMode.Inactive -> null
        is OfflineMode.Preparing -> OfflineStatusView(
            "Getting your offline copy ready",
            "Your reviews, collections, profile and notifications are being saved on this phone. Keep the app open while you're online.",
            StatusTone.Neutral,
        )
        is OfflineMode.Active -> OfflineStatusView(
            "Ready to use offline",
            listOfNotNull(waiting?.let { "$it waiting to sync." }, lastSync).joinToString(" ") + graceNote,
            if (waiting == null) StatusTone.Good else StatusTone.Neutral,
        )
        is OfflineMode.ReadOnly -> OfflineStatusView(
            "Reconnect to keep editing",
            "Your Premium subscription couldn't be confirmed for ${OfflineAccess.GRACE_DAYS} days. " +
                "You can still read what's saved on this phone; editing works again once you're online.",
            StatusTone.Warning,
        )
        is OfflineMode.Draining -> OfflineStatusView(
            "Sending your last changes",
            "${waiting ?: "Some changes"} made offline will still be sent when you're online, " +
                "then the offline copy is removed from this phone.",
            StatusTone.Warning,
        )
    }
}
