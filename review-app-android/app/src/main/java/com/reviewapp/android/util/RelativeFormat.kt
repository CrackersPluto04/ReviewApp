package com.reviewapp.android.util

import java.util.Locale

private const val MINUTE = 60_000L
private const val HOUR = 60 * MINUTE
private const val DAY = 24 * HOUR

/** "just now", "5 minutes ago", "3 hours ago", "2 days ago" (a time in the future counts as just now). */
fun formatAgo(thenMillis: Long, nowMillis: Long): String {
    val passed = nowMillis - thenMillis
    return when {
        passed < MINUTE -> "just now"
        passed < HOUR -> "${pluralize((passed / MINUTE).toInt(), "minute")} ago"
        passed < DAY -> "${pluralize((passed / HOUR).toInt(), "hour")} ago"
        else -> "${pluralize((passed / DAY).toInt(), "day")} ago"
    }
}

/** A duration left, rounded down to the largest unit: "6 days", "5 hours", "12 minutes", "less than a minute". */
fun formatTimeLeft(millis: Long): String = when {
    millis >= DAY -> pluralize((millis / DAY).toInt(), "day")
    millis >= HOUR -> pluralize((millis / HOUR).toInt(), "hour")
    millis >= MINUTE -> pluralize((millis / MINUTE).toInt(), "minute")
    else -> "less than a minute"
}

/** Storage sizes as the settings show them: "0 B", "512 KB", "12.3 MB", "1.2 GB". */
fun formatBytes(bytes: Long): String {
    val kb = 1024.0
    return when {
        bytes < kb -> "$bytes B"
        bytes < kb * kb -> "${(bytes / kb).toLong()} KB"
        bytes < kb * kb * kb -> String.format(Locale.US, "%.1f MB", bytes / (kb * kb))
        else -> String.format(Locale.US, "%.1f GB", bytes / (kb * kb * kb))
    }
}
