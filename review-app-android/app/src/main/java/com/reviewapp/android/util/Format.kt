package com.reviewapp.android.util

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale

const val MIN_SCORE = 1.0
const val MAX_SCORE = 10.0
const val SCORE_STEP = 0.5

/** Same thresholds and labels as the web app's review cards. */
enum class ScoreTier(val label: String) { Great("Great"), Mixed("Mixed"), Poor("Poor") }

fun scoreTier(score: Double): ScoreTier = when {
    score >= 7.0 -> ScoreTier.Great
    score >= 4.0 -> ScoreTier.Mixed
    else -> ScoreTier.Poor
}

/** 8.0 -> "8", 7.5 -> "7.5" */
fun formatScore(score: Double): String =
    if (score % 1.0 == 0.0) score.toLong().toString() else String.format(Locale.US, "%.1f", score)

/** Rounds to the closest valid slider value (1-10, step 0.5). */
fun snapScore(value: Double): Double =
    (Math.round(value / SCORE_STEP) * SCORE_STEP).coerceIn(MIN_SCORE, MAX_SCORE)

private val displayDateFormatter = DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM)

/**
 * Formats the API's dates for display. It accepts `yyyy-MM-dd`, full ISO date-times and, for
 * TMDb/Spotify release dates, a bare year or `yyyy-MM`. Anything else is shown as it came.
 */
fun formatDate(raw: String?): String? {
    if (raw.isNullOrBlank()) return null
    if (raw.length < 10) return raw
    return try {
        LocalDate.parse(raw.substring(0, 10)).format(displayDateFormatter)
    } catch (_: Exception) {
        raw
    }
}

/** True when two API dates fall on different days. */
fun isDifferentDay(first: String, second: String): Boolean = first.take(10) != second.take(10)

fun pluralize(count: Int, singular: String, plural: String = singular + "s") =
    "$count ${if (count == 1) singular else plural}"

private val displayDateTimeFormatter = DateTimeFormatter.ofLocalizedDateTime(FormatStyle.MEDIUM, FormatStyle.SHORT)

/**
 * Formats an ISO instant (e.g. a notification's `2026-10-14T12:34:56.1234567Z`) as a local date
 * and time. Anything without a time zone goes through [formatDate].
 */
fun formatDateTime(raw: String?, zone: ZoneId = ZoneId.systemDefault()): String? {
    if (raw.isNullOrBlank()) return null
    return try {
        OffsetDateTime.parse(raw).atZoneSameInstant(zone).format(displayDateTimeFormatter)
    } catch (_: Exception) {
        formatDate(raw)
    }
}
