package com.reviewapp.android.data.sync

/**
 * Whether the logged-in user's writes and reads go through the device's offline copy. Free users and users with
 * offline mode switched off are [Inactive]: the app works exactly as it did before offline mode.
 */
sealed interface OfflineMode {
    /** The user the mode is about; `null` when [Inactive] (logged out too). */
    val userId: Int?

    /** The app works as it did before offline mode (Free, switched off, logged out). */
    data object Inactive : OfflineMode {
        override val userId: Int? = null
    }

    /** Switched on, the first download of the user's own content hasn't finished yet. */
    data class Preparing(override val userId: Int) : OfflineMode

    /** Edits work with or without a connection. */
    data class Active(override val userId: Int) : OfflineMode

    /**
     * The grace period is over without the backend confirming the session and Premium: the offline copy can be
     * read, but editing waits until the app reaches the backend.
     */
    data class ReadOnly(override val userId: Int) : OfflineMode

    /**
     * Switched off, or Premium ended, while changes are still waiting: they are still sent (they belong to the
     * user, not to the subscription), then the offline copy is removed.
     */
    data class Draining(override val userId: Int) : OfflineMode

    /** A local copy exists for this mode (the database is open). */
    val usesLocalCopy: Boolean get() = this !is Inactive

    /** Whether writes go to the offline copy and the outbox. */
    val canEditOffline: Boolean get() = this is Active
}

/** The inputs of [OfflineModeRules.decide], all about the logged-in user. */
data class OfflineModeInput(
    val userId: Int?,
    /** `offline-mode` is in the user's entitlements (as last confirmed). */
    val entitled: Boolean,
    /** The user's offline mode switch. */
    val enabled: Boolean,
    /** The backend confirmed the session within the grace period (always true while verified). */
    val withinGrace: Boolean,
    /** The first full download finished. */
    val prepared: Boolean,
    /** A database file exists for the user. */
    val hasLocalCopy: Boolean,
    val pendingChanges: Int,
    /** The entitlement comes from the backend in this run, not from the device's cache. */
    val verified: Boolean,
)

/** What the gateway does besides showing the mode. */
data class OfflineModeDecision(
    val mode: OfflineMode,
    /** The local copy has nothing left to send and isn't wanted any more: delete it. */
    val deleteLocalCopy: Boolean = false,
)

/** The pure decision of the offline mode, unit-tested. */
object OfflineModeRules {

    /** The mode for the logged-in user's situation, and whether the local copy should be deleted. */
    fun decide(input: OfflineModeInput): OfflineModeDecision {
        val userId = input.userId ?: return OfflineModeDecision(OfflineMode.Inactive)

        if (input.entitled && input.enabled) {
            val mode = when {
                !input.withinGrace -> OfflineMode.ReadOnly(userId)
                !input.prepared -> OfflineMode.Preparing(userId)
                else -> OfflineMode.Active(userId)
            }
            return OfflineModeDecision(mode)
        }

        // Not (or no longer) offline: switched off by the user, or Premium ended
        if (!input.hasLocalCopy) return OfflineModeDecision(OfflineMode.Inactive)
        if (input.pendingChanges > 0) return OfflineModeDecision(OfflineMode.Draining(userId))
        // A lapse is only believed when the backend said so; switching off is the user's own decision
        val wantsDeletion = !input.enabled || input.verified
        return OfflineModeDecision(OfflineMode.Inactive, deleteLocalCopy = wantsDeletion)
    }
}
