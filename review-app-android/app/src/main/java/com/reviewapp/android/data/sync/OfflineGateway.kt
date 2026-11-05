package com.reviewapp.android.data.sync

import android.util.Log
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionHealth
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.local.offline.DatabaseManager
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.SyncMetaKeys
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.FeatureKeys
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.emitAll
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** What the offline settings screen and the connectivity strip show. */
data class OfflineStatus(
    val mode: OfflineMode = OfflineMode.Inactive,
    /** Changes the backend hasn't accepted yet. */
    val pendingChanges: Int = 0,
    /** Device time (epoch millis) of the last finished sync. */
    val lastSyncedAt: Long? = null,
    /** Millis left of the grace period while the session isn't verified (null while verified). */
    val graceLeftMillis: Long? = null,
    /** Device time (epoch millis) when "Make available offline" last finished. */
    val libraryDownloadedAt: Long? = null,
    /** Of [pendingChanges], the ones in Sync issues (the rest are still to be sent). */
    val issues: Int = 0,
) {
    val toSend: Int get() = (pendingChanges - issues).coerceAtLeast(0)
}

/**
 * Decides, for the logged-in user, whether the offline path is used ([OfflineMode], rules in [OfflineModeRules]),
 * and keeps the user's offline database open while it is: repositories ask [database] and get null whenever the
 * app should behave as it did before offline mode (Free users, switched off, logged out).
 *
 * It also removes local copies that aren't wanted any more: when the mode was switched off or Premium ended
 * (confirmed by the backend) and nothing is waiting to be sent, and on an explicit logout without waiting changes.
 * A local copy with waiting changes is never deleted here (the user decides, see the logout warning).
 */
@OptIn(ExperimentalCoroutinesApi::class)
class OfflineGateway(
    private val sessionManager: SessionManager,
    private val settings: SettingsStore,
    private val databases: DatabaseManager,
    private val clock: DeviceClock,
    private val scope: CoroutineScope,
) {
    private val _status = MutableStateFlow(OfflineStatus())
    val status: StateFlow<OfflineStatus> = _status.asStateFlow()

    val mode: OfflineMode get() = _status.value.mode

    private val lock = Mutex()
    /** Bumped after a local copy was deleted, so the inputs are read again. */
    private val localCopyGeneration = MutableStateFlow(0)

    /** The logged-in user's offline database while the mode uses one, else null (the online-only path). */
    fun database(): OfflineDatabase? {
        val mode = _status.value.mode
        val open = databases.current.value ?: return null
        return open.db.takeIf { mode.usesLocalCopy && mode.userId == open.userId }
    }

    fun start() {
        scope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user }
                // Only who it is and what they may use matter here
                .distinctUntilChanged { a, b -> a?.id == b?.id && a?.entitlements == b?.entitlements }
                .flatMapLatest { user -> if (user == null) flowOf(null) else inputsOf(user) }
                .collect { inputs -> apply(inputs) }
        }
        scope.launch {
            sessionManager.explicitLogouts.collect { userId -> onExplicitLogout(userId) }
        }
    }

    private class Inputs(
        val input: OfflineModeInput,
        val lastSyncedAt: Long?,
        val graceLeftMillis: Long?,
        val libraryDownloadedAt: Long? = null,
        val issues: Int = 0,
    )

    private fun inputsOf(user: AuthUser): Flow<Inputs> {
        val entitled = FeatureKeys.OFFLINE_MODE in user.entitlements
        return combine(
            settings.offlineEnabled(user.id),
            sessionManager.health,
            sessionManager.confirmedAt,
            localCopyGeneration,
            sessionManager.health.flatMapLatest { if (it == SessionHealth.Verified) flowOf(Unit) else minuteTicker() },
        ) { enabled, health, confirmedAt, _, _ -> Base(enabled, health, confirmedAt) }
            .flatMapLatest { base ->
                val now = clock.now()
                val graceLeft = if (base.health == SessionHealth.Verified) null else base.confirmedAt?.let { OfflineAccess.remaining(it, now) } ?: 0L
                fun input(prepared: Boolean, hasLocalCopy: Boolean, pending: Int) = OfflineModeInput(
                    userId = user.id,
                    entitled = entitled,
                    enabled = base.enabled,
                    withinGrace = graceLeft == null || graceLeft > 0,
                    prepared = prepared,
                    hasLocalCopy = hasLocalCopy,
                    pendingChanges = pending,
                    verified = base.health == SessionHealth.Verified,
                )

                // A copy is opened when the mode wants one, or when an old one may still have changes to send
                if ((entitled && base.enabled) || databases.exists(user.id)) {
                    flow {
                        val db = lock.withLock { databases.open(user.id) }
                        emitAll(
                            combine(
                                db.syncMeta().observe(SyncMetaKeys.PREPARED),
                                db.syncMeta().observe(SyncMetaKeys.LAST_SYNCED_AT),
                                db.syncMeta().observe(SyncMetaKeys.LIBRARY_DOWNLOADED_AT),
                                db.outbox().observeCount(),
                                db.outbox().observeIssueCount(),
                            ) { prepared, lastSynced, downloaded, pending, issues ->
                                Inputs(
                                    input(prepared == "true", hasLocalCopy = true, pending),
                                    lastSynced?.toLongOrNull(),
                                    graceLeft,
                                    downloaded?.toLongOrNull(),
                                    issues,
                                )
                            },
                        )
                    }
                        // The copy was closed or deleted under the query; the next generation reads it again
                        .catch { Log.w(TAG, "Offline database closed while observed", it) }
                } else {
                    flowOf(Inputs(input(prepared = false, hasLocalCopy = false, pending = 0), null, graceLeft))
                }
            }
    }

    private data class Base(val enabled: Boolean, val health: SessionHealth, val confirmedAt: ClockReading?)

    private suspend fun apply(inputs: Inputs?) {
        if (inputs == null) {
            _status.value = OfflineStatus()
            lock.withLock { databases.close() }
            return
        }
        val decision = OfflineModeRules.decide(inputs.input)
        _status.value = OfflineStatus(
            decision.mode, inputs.input.pendingChanges, inputs.lastSyncedAt, inputs.graceLeftMillis, inputs.libraryDownloadedAt,
            inputs.issues,
        )
        val userId = inputs.input.userId
        if (decision.deleteLocalCopy && userId != null) {
            lock.withLock { databases.delete(userId) }
            localCopyGeneration.value++
        }
    }

    /** Logged out on purpose: the offline copy goes too, unless changes are still waiting in it. */
    private suspend fun onExplicitLogout(userId: Int) = lock.withLock {
        if (!databases.exists(userId)) return@withLock
        try {
            val pending = databases.open(userId).outbox().count()
            if (pending == 0) databases.delete(userId) else databases.close()
        } catch (e: Exception) {
            Log.w(TAG, "Could not check the offline copy after logout", e)
        }
    }

    // An unverified session's grace period runs out without any other input changing
    private fun minuteTicker(): Flow<Unit> = flow {
        while (true) {
            emit(Unit)
            delay(60_000)
        }
    }

    private companion object {
        const val TAG = "OfflineGateway"
    }
}
