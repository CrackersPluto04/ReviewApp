package com.reviewapp.android.data.sync

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class OfflineModeRulesTest {

    private val premiumOn = OfflineModeInput(
        userId = 7,
        entitled = true,
        enabled = true,
        withinGrace = true,
        prepared = true,
        hasLocalCopy = true,
        pendingChanges = 0,
        verified = true,
    )

    private fun decide(input: OfflineModeInput) = OfflineModeRules.decide(input)

    @Test
    fun loggedOut_IsInactive() {
        assertEquals(OfflineModeDecision(OfflineMode.Inactive), decide(premiumOn.copy(userId = null)))
    }

    @Test
    fun premiumAndOn_IsActive_OncePrepared() {
        assertEquals(OfflineMode.Active(7), decide(premiumOn).mode)
        assertEquals(OfflineMode.Preparing(7), decide(premiumOn.copy(prepared = false, hasLocalCopy = false)).mode)
        assertTrue(decide(premiumOn).mode.canEditOffline)
    }

    @Test
    fun pastTheGracePeriod_IsReadOnly_AndNothingIsDeleted() {
        val decision = decide(premiumOn.copy(withinGrace = false, verified = false, pendingChanges = 3))

        assertEquals(OfflineMode.ReadOnly(7), decision.mode)
        assertFalse(decision.mode.canEditOffline)
        assertFalse(decision.deleteLocalCopy)
    }

    @Test
    fun freeUserWithoutACopy_IsInactive() {
        assertEquals(
            OfflineModeDecision(OfflineMode.Inactive),
            decide(premiumOn.copy(entitled = false, prepared = false, hasLocalCopy = false)),
        )
    }

    @Test
    fun switchedOff_WithWaitingChanges_Drains_ThenTheCopyIsDeleted() {
        val off = premiumOn.copy(enabled = false)

        assertEquals(OfflineModeDecision(OfflineMode.Draining(7)), decide(off.copy(pendingChanges = 2)))
        assertEquals(OfflineModeDecision(OfflineMode.Inactive, deleteLocalCopy = true), decide(off))
    }

    @Test
    fun premiumEnded_WithWaitingChanges_StillSendsThem() {
        val lapsed = premiumOn.copy(entitled = false)

        assertEquals(OfflineMode.Draining(7), decide(lapsed.copy(pendingChanges = 1)).mode)
        assertEquals(OfflineModeDecision(OfflineMode.Inactive, deleteLocalCopy = true), decide(lapsed))
    }

    @Test
    fun aLapseSeenOnlyInTheDevicesCache_NeverDeletesTheCopy() {
        // Opened offline with an old cached user: wait for the backend to confirm before deleting anything
        val decision = decide(premiumOn.copy(entitled = false, verified = false))

        assertEquals(OfflineMode.Inactive, decision.mode)
        assertFalse(decision.deleteLocalCopy)
    }
}
