package com.reviewapp.android.data.sync

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OfflineAccessTest {

    private val hour = 60 * 60 * 1000L
    private val day = 24 * hour
    private val confirmed = ClockReading(wallMillis = 1_000 * day, elapsedMillis = 5 * hour, bootCount = 3)

    private fun later(wall: Long, elapsed: Long, boot: Int = 3) = ClockReading(confirmed.wallMillis + wall, elapsed, boot)

    @Test
    fun sameBoot_TheMonotonicClockDecides_WhateverTheWallClockSays() {
        // Six days of uptime since the confirmation, the wall clock set back a month
        val now = later(wall = -30 * day, elapsed = confirmed.elapsedMillis + 6 * day)

        assertEquals(6 * day, OfflineAccess.elapsedSince(confirmed, now))
        assertTrue(OfflineAccess.isWithinGrace(confirmed, now))

        val eightDays = later(wall = -30 * day, elapsed = confirmed.elapsedMillis + 8 * day)
        assertFalse(OfflineAccess.isWithinGrace(confirmed, eightDays))
    }

    @Test
    fun theGracePeriodIsSevenDays_EndExclusive() {
        val justBefore = later(wall = 0, elapsed = confirmed.elapsedMillis + OfflineAccess.GRACE_MILLIS - 1)
        val atTheEnd = later(wall = 0, elapsed = confirmed.elapsedMillis + OfflineAccess.GRACE_MILLIS)

        assertTrue(OfflineAccess.isWithinGrace(confirmed, justBefore))
        assertFalse(OfflineAccess.isWithinGrace(confirmed, atTheEnd))
        assertEquals(1, OfflineAccess.remaining(confirmed, justBefore))
        assertEquals(0, OfflineAccess.remaining(confirmed, atTheEnd))
    }

    @Test
    fun afterAReboot_TheWallClockDecides() {
        val now = later(wall = 2 * day, elapsed = hour, boot = 4)

        assertEquals(2 * day, OfflineAccess.elapsedSince(confirmed, now))
        assertTrue(OfflineAccess.isWithinGrace(confirmed, now))
        assertFalse(OfflineAccess.isWithinGrace(confirmed, later(wall = 7 * day, elapsed = hour, boot = 4)))
    }

    @Test
    fun afterAReboot_TheUptimeIsALowerBound_SoAClockSetBackStillCountsIt() {
        // Rebooted, then the clock was set to just after the confirmation; 8 days of uptime since the boot
        val now = later(wall = hour, elapsed = 8 * day, boot = 4)

        assertEquals(8 * day, OfflineAccess.elapsedSince(confirmed, now))
        assertFalse(OfflineAccess.isWithinGrace(confirmed, now))
    }

    @Test
    fun aWallClockBehindTheConfirmation_AfterAReboot_IsExpired() {
        val now = later(wall = -2 * day, elapsed = hour, boot = 4)

        assertNull(OfflineAccess.elapsedSince(confirmed, now))
        assertFalse(OfflineAccess.isWithinGrace(confirmed, now))
        assertEquals(0, OfflineAccess.remaining(confirmed, now))
    }

    @Test
    fun aSmallBackwardsCorrection_IsTolerated() {
        val now = later(wall = -OfflineAccess.CLOCK_TOLERANCE_MILLIS, elapsed = 10_000, boot = 4)

        assertEquals(10_000L, OfflineAccess.elapsedSince(confirmed, now))
    }

    @Test
    fun anUnknownBootCount_NeverCountsAsTheSameBoot() {
        val unknown = confirmed.copy(bootCount = -1)
        // Same elapsed numbers but no boot count: the wall clock (and the uptime bound) decides
        val now = ClockReading(unknown.wallMillis + 3 * day, unknown.elapsedMillis + hour, -1)

        assertEquals(3 * day, OfflineAccess.elapsedSince(unknown, now))
    }
}
