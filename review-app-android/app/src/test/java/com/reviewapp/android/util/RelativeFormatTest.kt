package com.reviewapp.android.util

import org.junit.Assert.assertEquals
import org.junit.Test

class RelativeFormatTest {

    private val minute = 60_000L
    private val hour = 60 * minute
    private val day = 24 * hour

    @Test
    fun ago_UsesTheLargestWholeUnit() {
        val now = 100 * day
        assertEquals("just now", formatAgo(now - 59_000, now))
        assertEquals("just now", formatAgo(now + hour, now))
        assertEquals("1 minute ago", formatAgo(now - minute, now))
        assertEquals("59 minutes ago", formatAgo(now - hour + 1, now))
        assertEquals("2 hours ago", formatAgo(now - 2 * hour - 5 * minute, now))
        assertEquals("1 day ago", formatAgo(now - day, now))
        assertEquals("12 days ago", formatAgo(now - 12 * day, now))
    }

    @Test
    fun timeLeft_RoundsDown() {
        assertEquals("6 days", formatTimeLeft(7 * day - 1))
        assertEquals("1 hour", formatTimeLeft(hour + 59 * minute))
        assertEquals("5 minutes", formatTimeLeft(5 * minute))
        assertEquals("less than a minute", formatTimeLeft(59_000))
    }

    @Test
    fun bytes_InBinaryUnits() {
        assertEquals("0 B", formatBytes(0))
        assertEquals("1023 B", formatBytes(1023))
        assertEquals("512 KB", formatBytes(512 * 1024))
        assertEquals("12.3 MB", formatBytes((12.3 * 1024 * 1024).toLong()))
        assertEquals("1.5 GB", formatBytes(1536L * 1024 * 1024))
    }
}
