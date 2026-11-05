package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.OfflineMode
import com.reviewapp.android.data.sync.OfflineStatus
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// The connectivity strip and the offline settings' status card
class OfflineTextsTest {

    private val active = OfflineStatus(OfflineMode.Active(7))
    private val hour = 60 * 60 * 1000L

    @Test
    fun strip_IsHiddenOnline_UnlessChangesWait() {
        assertNull(connectivityBanner(Connectivity.Online, OfflineStatus()))
        assertNull(connectivityBanner(Connectivity.Online, active))
        assertEquals(
            ConnectivityBanner("2 changes waiting to sync", disconnected = false, opensChanges = true),
            connectivityBanner(Connectivity.Online, active.copy(pendingChanges = 2)),
        )
    }

    @Test
    fun strip_Online_SaysWhatTheSyncDoes() {
        val waiting = active.copy(pendingChanges = 3)
        assertEquals("Syncing 3 changes…", connectivityBanner(Connectivity.Online, waiting, SyncActivity(running = true))!!.text)
        assertEquals("Sync paused, it continues shortly", connectivityBanner(Connectivity.Online, waiting, SyncActivity(paused = true))!!.text)

        // Only issues left: they need the user
        val issues = connectivityBanner(Connectivity.Online, active.copy(pendingChanges = 2, issues = 2))!!
        assertEquals("2 changes need your attention", issues.text)
        assertTrue(issues.problem && issues.opensChanges)
        assertEquals("1 change needs your attention", connectivityBanner(Connectivity.Online, active.copy(pendingChanges = 1, issues = 1))!!.text)
    }

    @Test
    fun strip_Offline_DependsOnTheMode() {
        assertEquals(ConnectivityBanner("You're offline"), connectivityBanner(Connectivity.Offline, OfflineStatus()))
        assertEquals(ConnectivityBanner("Offline · showing what's saved on this phone"), connectivityBanner(Connectivity.Offline, active))
        assertEquals(
            ConnectivityBanner("Offline · 1 change waiting to sync", opensChanges = true),
            connectivityBanner(Connectivity.Offline, active.copy(pendingChanges = 1)),
        )
        assertEquals(
            ConnectivityBanner("Offline · reconnect to keep editing", problem = true),
            connectivityBanner(Connectivity.Offline, OfflineStatus(OfflineMode.ReadOnly(7))),
        )
    }

    @Test
    fun strip_BackendDown_IsAProblem() {
        val banner = connectivityBanner(Connectivity.ServerUnreachable, active.copy(pendingChanges = 3))!!

        assertEquals("Can't reach ReviewApp right now · 3 changes waiting to sync", banner.text)
        assertTrue(banner.problem)
    }

    @Test
    fun status_Active_SaysWhatWaitsAndWhenItLastSynced() {
        val now = 10 * 24 * hour
        val view = offlineStatusView(active.copy(pendingChanges = 2, lastSyncedAt = now - 3 * hour), now)!!

        assertEquals("Ready to use offline", view.title)
        assertEquals("2 changes waiting to sync. Last synced 3 hours ago.", view.message)
        assertEquals(StatusTone.Neutral, view.tone)
        assertEquals("Not synced yet.", offlineStatusView(active, now)!!.message)
        assertEquals(StatusTone.Good, offlineStatusView(active, now)!!.tone)
    }

    @Test
    fun status_Unverified_SaysHowLongEditingStillWorks() {
        val view = offlineStatusView(active.copy(lastSyncedAt = 0, graceLeftMillis = 3 * 24 * hour + 5 * hour), 60_000)!!

        assertTrue(view.message, view.message.endsWith("Connect within 3 days to keep editing offline."))
    }

    @Test
    fun status_IsNothingWhenInactive_AndAWarningWhenDrainingOrReadOnly() {
        assertNull(offlineStatusView(OfflineStatus(), 0))
        assertEquals(StatusTone.Warning, offlineStatusView(OfflineStatus(OfflineMode.ReadOnly(7)), 0)!!.tone)
        val draining = offlineStatusView(OfflineStatus(OfflineMode.Draining(7), pendingChanges = 4), 0)!!
        assertTrue(draining.message, draining.message.startsWith("4 changes made offline will still be sent"))
    }
}
