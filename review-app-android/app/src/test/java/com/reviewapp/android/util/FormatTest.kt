package com.reviewapp.android.util

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.ZoneId

class FormatTest {

    @Test
    fun scoreTier_usesWebThresholds() {
        assertEquals(ScoreTier.Great, scoreTier(7.0))
        assertEquals(ScoreTier.Mixed, scoreTier(6.5))
        assertEquals(ScoreTier.Mixed, scoreTier(4.0))
        assertEquals(ScoreTier.Poor, scoreTier(3.5))
    }

    @Test
    fun formatScore_dropsTrailingZero() {
        assertEquals("8", formatScore(8.0))
        assertEquals("7.5", formatScore(7.5))
        assertEquals("10", formatScore(10.0))
    }

    @Test
    fun snapScore_roundsToHalfStepsWithinRange() {
        assertEquals(7.5, snapScore(7.4), 0.0)
        assertEquals(7.0, snapScore(7.1), 0.0)
        assertEquals(1.0, snapScore(0.2), 0.0)
        assertEquals(10.0, snapScore(11.0), 0.0)
    }

    @Test
    fun formatDate_handlesApiFormats() {
        assertNull(formatDate(null))
        assertNull(formatDate(""))
        // Bare year / year-month from Spotify are kept as they are
        assertEquals("1999", formatDate("1999"))
        assertEquals("1999-05", formatDate("1999-05"))
        // A date-time and a plain date of the same day format the same way
        assertEquals(formatDate("2026-10-13"), formatDate("2026-10-13T12:34:56.789"))
    }

    @Test
    fun formatDateTime_convertsInstantToLocalZone() {
        assertNull(formatDateTime(null))
        // Up to 7 fractional digits, like the API sends
        val raw = "2026-10-14T23:30:00.1234567Z"
        assertTrue(formatDateTime(raw, ZoneId.of("UTC"))!!.contains(formatDate("2026-10-14")!!))
        // Already the next day in Budapest (UTC+2)
        assertTrue(formatDateTime(raw, ZoneId.of("Europe/Budapest"))!!.contains(formatDate("2026-10-15")!!))
        // Without a zone it falls back to the date only
        assertEquals(formatDate("2026-10-14"), formatDateTime("2026-10-14"))
    }

    @Test
    fun isDifferentDay_comparesDatePartOnly() {
        assertFalse(isDifferentDay("2026-10-13T08:00:00", "2026-10-13T20:00:00"))
        assertTrue(isDifferentDay("2026-10-13T08:00:00", "2026-10-14T08:00:00"))
    }

    @Test
    fun pluralize_picksForm() {
        assertEquals("1 review", pluralize(1, "review"))
        assertEquals("0 reviews", pluralize(0, "review"))
        assertEquals("2 replies", pluralize(2, "reply", "replies"))
    }
}
