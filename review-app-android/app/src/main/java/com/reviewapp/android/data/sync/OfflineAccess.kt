package com.reviewapp.android.data.sync

import kotlinx.serialization.Serializable

/**
 * One reading of the device's clocks: the wall clock (UTC millis, the user can change it), the time since boot
 * (`elapsedRealtime`, can't be changed, restarts at 0 on a reboot) and the boot count (tells the two cases apart).
 */
@Serializable
data class ClockReading(val wallMillis: Long, val elapsedMillis: Long, val bootCount: Int)

/** The source of [ClockReading]s; a lambda in the app, a fake in tests. */
fun interface DeviceClock {
    fun now(): ClockReading
}

/**
 * The offline grace period: offline mode (and an offline start) works for [GRACE_MILLIS] after the backend last
 * confirmed the session and the Premium status. Measured so that moving the clock back can't extend it:
 * within one boot the monotonic clock decides; after a reboot the wall clock does, but never less than this
 * boot's uptime, and a wall clock behind the confirmation (more than [CLOCK_TOLERANCE_MILLIS]) counts as expired.
 */
object OfflineAccess {
    /** How long the app may be used offline since the backend last confirmed the session. */
    const val GRACE_DAYS = 7

    /** [GRACE_DAYS] in milliseconds. */
    const val GRACE_MILLIS = GRACE_DAYS * 24L * 60 * 60 * 1000
    /** Small backwards steps are normal (network time corrections). */
    const val CLOCK_TOLERANCE_MILLIS = 5L * 60 * 1000

    /** Time passed since [confirmed], or null when the clocks can't be trusted (treated as expired). */
    fun elapsedSince(confirmed: ClockReading, now: ClockReading): Long? {
        val sameBoot = confirmed.bootCount >= 0 && confirmed.bootCount == now.bootCount &&
            now.elapsedMillis >= confirmed.elapsedMillis
        if (sameBoot) return now.elapsedMillis - confirmed.elapsedMillis

        val wall = now.wallMillis - confirmed.wallMillis
        if (wall < -CLOCK_TOLERANCE_MILLIS) return null
        return maxOf(wall, now.elapsedMillis)
    }

    /** Whether the grace period still runs; untrustworthy clocks count as over. */
    fun isWithinGrace(confirmed: ClockReading, now: ClockReading): Boolean =
        elapsedSince(confirmed, now)?.let { it < GRACE_MILLIS } ?: false

    /** Millis left of the grace period (0 when over), for "Reconnect within 3 days" texts. */
    fun remaining(confirmed: ClockReading, now: ClockReading): Long =
        elapsedSince(confirmed, now)?.let { (GRACE_MILLIS - it).coerceAtLeast(0) } ?: 0
}
