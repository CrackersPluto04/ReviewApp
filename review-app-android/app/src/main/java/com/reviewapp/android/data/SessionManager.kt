package com.reviewapp.android.data

import com.reviewapp.android.data.local.CachedSession
import com.reviewapp.android.data.local.SecureCookieJar
import com.reviewapp.android.data.local.SessionCache
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.sync.ClockReading
import com.reviewapp.android.data.sync.DeviceClock
import com.reviewapp.android.data.sync.OfflineAccess
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.FeatureKeys
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

sealed interface AuthState {
    /** The stored session is being checked on app start. */
    data object Loading : AuthState
    data object LoggedOut : AuthState
    data class LoggedIn(val user: AuthUser) : AuthState
}

/**
 * Global auth state, the Android counterpart of the web app's `AuthContext`.
 * The session itself is the cookie in [SecureCookieJar]; this class only tracks who it belongs to.
 *
 * Offline mode: the last user the backend confirmed is kept in [SessionCache] with the time of that confirmation.
 * A user with offline mode starts logged in without the backend within the grace period ([SessionRestore],
 * [OfflineAccess]); [health] then says how sure that is, and the session is checked as soon as the backend is
 * reachable ([restoreIfNeeded]). Logging out (or a refused session) never deletes the offline copy here: that is
 * the offline gateway's decision.
 */
class SessionManager(
    private val authApi: AuthApi,
    private val cookieJar: SecureCookieJar,
    private val cache: SessionCache,
    private val clock: DeviceClock,
    private val scope: CoroutineScope,
    /** The backend can probably be reached (a validated network, no failure seen). */
    private val isOnline: () -> Boolean,
    /** The user's offline mode switch. */
    private val offlineEnabled: suspend (userId: Int) -> Boolean,
) {
    private val _state = MutableStateFlow<AuthState>(AuthState.Loading)
    val state: StateFlow<AuthState> = _state.asStateFlow()

    private val _health = MutableStateFlow(SessionHealth.Verified)
    val health: StateFlow<SessionHealth> = _health.asStateFlow()

    /** When the backend last confirmed the logged-in user (the start of the offline grace period). */
    private val _confirmedAt = MutableStateFlow<ClockReading?>(null)
    val confirmedAt: StateFlow<ClockReading?> = _confirmedAt.asStateFlow()

    /** The user id of every logout the user asked for (here or `logout-all` from this device). */
    private val _explicitLogouts = MutableSharedFlow<Int>(extraBufferCapacity = 4)
    val explicitLogouts: SharedFlow<Int> = _explicitLogouts.asSharedFlow()

    private val restoreLock = Mutex()

    val currentUser: AuthUser? get() = (_state.value as? AuthState.LoggedIn)?.user

    /** Checks the stored cookie with the API on app start. On a network error the cookie is kept for the next try. */
    suspend fun restore() = restoreLock.withLock { restoreLocked() }

    private suspend fun restoreLocked() {
        val hasSession = withContext(Dispatchers.IO) { cookieJar.hasSession() }
        val check = if (hasSession) check() else null
        val cached = cache.read()
        val outcome = SessionRestore.decide(
            check = check,
            cached = cached,
            offlineStartAllowed = cached != null && offlineStartAllowed(cached),
            networkAvailable = isOnline(),
        )
        when (outcome) {
            is RestoreOutcome.LoggedIn -> {
                _health.value = outcome.health
                if (outcome.health == SessionHealth.Verified) confirm(outcome.user) else _confirmedAt.value = cached?.confirmedAt
                _state.value = AuthState.LoggedIn(outcome.user)
            }
            is RestoreOutcome.LoggedOut -> {
                if (outcome.clearCookie) withContext(Dispatchers.IO) { cookieJar.clear() }
                if (outcome.clearCache) cache.clear()
                setLoggedOut()
            }
        }
    }

    private suspend fun check(): SessionCheck = when (val result = apiCall { authApi.checkAuth() }) {
        is ApiResult.Success -> SessionCheck.Confirmed(result.data.toDomain())
        // 401: expired or revoked (logout-all, password change); 404: the user was deleted
        is ApiResult.Error -> if (result.code == 401 || result.code == 404) SessionCheck.Rejected else SessionCheck.Unreachable
    }

    private suspend fun offlineStartAllowed(cached: CachedSession): Boolean =
        FeatureKeys.OFFLINE_MODE in cached.user.entitlements &&
            OfflineAccess.isWithinGrace(cached.confirmedAt, clock.now()) &&
            offlineEnabled(cached.user.id)

    /**
     * When the backend is reachable again: retries a failed start, checks a session opened offline, and asks for a
     * login when the offline session's cookie expired (the offline copy stays for after the login).
     */
    suspend fun restoreIfNeeded() = restoreLock.withLock {
        when (_state.value) {
            AuthState.LoggedOut -> if (withContext(Dispatchers.IO) { cookieJar.hasSession() }) restoreLocked()
            is AuthState.LoggedIn -> when (_health.value) {
                SessionHealth.Verified -> Unit
                SessionHealth.NeedsLogin -> if (isOnline()) setLoggedOut()
                SessionHealth.Unverified -> verifyLocked()
            }
            AuthState.Loading -> Unit
        }
    }

    private suspend fun verifyLocked() {
        val current = currentUser ?: return
        when (val check = check()) {
            is SessionCheck.Confirmed -> if (check.user.id == current.id) {
                _health.value = SessionHealth.Verified
                confirm(check.user)
                _state.value = AuthState.LoggedIn(check.user)
            }
            SessionCheck.Rejected -> {
                withContext(Dispatchers.IO) { cookieJar.clear() }
                cache.clear()
                setLoggedOut()
            }
            SessionCheck.Unreachable -> Unit
        }
    }

    suspend fun onLoggedIn(user: AuthUser) {
        _health.value = SessionHealth.Verified
        confirm(user)
        _state.value = AuthState.LoggedIn(user)
    }

    private suspend fun confirm(user: AuthUser) {
        val now = clock.now()
        _confirmedAt.value = now
        cache.confirm(user, now)
    }

    /** Keeps the cached user in sync after a profile change (e.g. a new avatar). */
    fun updateUser(transform: (AuthUser) -> AuthUser) {
        _state.update { state -> if (state is AuthState.LoggedIn) AuthState.LoggedIn(transform(state.user)) else state }
        currentUser?.let { user -> scope.launch { cache.updateUser(user) } }
    }

    /**
     * Reloads the logged-in user (e.g. after verifying the email in the browser), like the web's
     * `refreshUser`. A revoked session gets a 401, which logs out through [onUnauthorized].
     */
    suspend fun refreshUser() {
        val current = currentUser ?: return
        val result = apiCall { authApi.checkAuth() }
        if (result is ApiResult.Success) {
            val user = result.data.toDomain()
            // Ignore it if someone else logged in meanwhile
            var replaced = false
            _state.update { state ->
                if (state is AuthState.LoggedIn && state.user.id == current.id) AuthState.LoggedIn(user).also { replaced = true } else state
            }
            if (replaced) {
                _health.value = SessionHealth.Verified
                confirm(user)
            }
        }
    }

    /** After `logout-all`: the API already revoked every session and deleted the cookie. */
    suspend fun onLoggedOutEverywhere() {
        val userId = currentUser?.id
        withContext(Dispatchers.IO) { cookieJar.clear() }
        cache.clear()
        setLoggedOut()
        userId?.let { _explicitLogouts.emit(it) }
    }

    /**
     * Called for every 401 response (see `UnauthorizedInterceptor`), like the web app's `apiFetch`. The session is
     * gone (expired or revoked), so it isn't reopened offline either; the user's offline copy stays on the device
     * for their next login. Runs on OkHttp's threads, so blocking is fine.
     */
    fun onUnauthorized() {
        if (_state.value is AuthState.LoggedIn) {
            cookieJar.clear()
            runBlocking { cache.clear() }
            setLoggedOut()
        }
    }

    suspend fun logout() {
        val userId = currentUser?.id
        // The API deletes the cookie, but it's cleared locally either way so logout always works offline
        apiCall { authApi.logout() }
        withContext(Dispatchers.IO) { cookieJar.clear() }
        cache.clear()
        setLoggedOut()
        userId?.let { _explicitLogouts.emit(it) }
    }

    private fun setLoggedOut() {
        _state.value = AuthState.LoggedOut
        _health.value = SessionHealth.Verified
        _confirmedAt.value = null
    }
}
