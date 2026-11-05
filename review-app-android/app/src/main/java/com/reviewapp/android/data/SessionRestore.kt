package com.reviewapp.android.data

import com.reviewapp.android.data.local.CachedSession
import com.reviewapp.android.domain.model.AuthUser

/** How sure the app is that the shown session is still valid on the backend (only meaningful while logged in). */
enum class SessionHealth {
    /** The backend confirmed it in this run of the app. */
    Verified,
    /** Opened offline from the device with a stored cookie; checked as soon as the backend is reachable. */
    Unverified,
    /**
     * Opened offline from the device, but the cookie expired meanwhile (sessions last 8 hours): the user can keep
     * working offline, and is asked to log in once the device is online.
     */
    NeedsLogin,
}

/** What `GET Auth/check-auth` answered on start. */
sealed interface SessionCheck {
    data class Confirmed(val user: AuthUser) : SessionCheck
    /** 401 (expired, revoked) or 404 (user deleted): the session is gone for sure. */
    data object Rejected : SessionCheck
    /** No answer (no network, timeout) or a server error: nothing is known. */
    data object Unreachable : SessionCheck
}

sealed interface RestoreOutcome {
    data class LoggedIn(val user: AuthUser, val health: SessionHealth) : RestoreOutcome
    data class LoggedOut(val clearCookie: Boolean, val clearCache: Boolean) : RestoreOutcome
}

/**
 * The app start decision. Without offline mode it is today's behaviour: a confirmed session logs in, everything
 * else is logged out (a rejected one also drops the cookie). With offline mode (the cached user may use it, see
 * [offlineStartAllowed]) an unreachable backend opens the cached user instead of the logged-out app.
 */
object SessionRestore {

    /**
     * @param check null when no cookie is stored (nothing was asked)
     * @param offlineStartAllowed the cached user has offline mode, it is switched on and the grace period runs
     * @param networkAvailable a validated network exists (only asked when no cookie is stored)
     */
    fun decide(
        check: SessionCheck?,
        cached: CachedSession?,
        offlineStartAllowed: Boolean,
        networkAvailable: Boolean,
    ): RestoreOutcome = when (check) {
        is SessionCheck.Confirmed -> RestoreOutcome.LoggedIn(check.user, SessionHealth.Verified)
        // A session the backend refused is never reopened offline either
        SessionCheck.Rejected -> RestoreOutcome.LoggedOut(clearCookie = true, clearCache = true)
        SessionCheck.Unreachable ->
            if (cached != null && offlineStartAllowed) RestoreOutcome.LoggedIn(cached.user.toDomain(), SessionHealth.Unverified)
            else RestoreOutcome.LoggedOut(clearCookie = false, clearCache = false)
        // The cookie expired: online that is a normal logout (the cache stays for a later offline start)
        null ->
            if (cached != null && offlineStartAllowed && !networkAvailable) RestoreOutcome.LoggedIn(cached.user.toDomain(), SessionHealth.NeedsLogin)
            else RestoreOutcome.LoggedOut(clearCookie = false, clearCache = false)
    }
}
