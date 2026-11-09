package com.reviewapp.android.data

import com.reviewapp.android.data.local.CachedSession
import com.reviewapp.android.data.local.CachedUser
import com.reviewapp.android.data.sync.ClockReading
import com.reviewapp.android.domain.model.AuthUser
import org.junit.Assert.assertEquals
import org.junit.Test

class SessionRestoreTest {

    private val confirmedUser = AuthUser(7, "fresh", null, isEmailConfirmed = true, isPremium = true, entitlements = setOf("offline-mode"))
    private val cached = CachedSession(
        CachedUser(7, "cached", isPremium = true, entitlements = setOf("offline-mode")),
        ClockReading(0, 0, 1),
    )

    private fun decide(check: SessionCheck?, cached: CachedSession? = this.cached, allowed: Boolean = true, network: Boolean = false) =
        SessionRestore.decide(check, cached, allowed, network)

    @Test
    fun aConfirmedSession_LogsInVerified_WithTheBackendsUser() {
        assertEquals(
            RestoreOutcome.LoggedIn(confirmedUser, SessionHealth.Verified),
            decide(SessionCheck.Confirmed(confirmedUser)),
        )
    }

    @Test
    fun aRejectedSession_LogsOut_AndIsNeverReopenedOffline() {
        assertEquals(RestoreOutcome.LoggedOut(clearCookie = true, clearCache = true), decide(SessionCheck.Rejected))
    }

    @Test
    fun unreachable_WithOfflineMode_OpensTheCachedUserUnverified() {
        assertEquals(
            RestoreOutcome.LoggedIn(cached.user.toDomain(), SessionHealth.Unverified),
            decide(SessionCheck.Unreachable),
        )
    }

    @Test
    fun unreachable_WithoutOfflineMode_IsTodaysLogout_KeepingTheCookie() {
        val keepAll = RestoreOutcome.LoggedOut(clearCookie = false, clearCache = false)
        assertEquals(keepAll, decide(SessionCheck.Unreachable, allowed = false))
        assertEquals(keepAll, decide(SessionCheck.Unreachable, cached = null))
    }

    @Test
    fun anExpiredCookie_Offline_OpensTheCachedUser_NeedingALoginLater() {
        assertEquals(
            RestoreOutcome.LoggedIn(cached.user.toDomain(), SessionHealth.NeedsLogin),
            decide(check = null, network = false),
        )
    }

    @Test
    fun anExpiredCookie_Online_IsANormalLogout() {
        val keepAll = RestoreOutcome.LoggedOut(clearCookie = false, clearCache = false)
        assertEquals(keepAll, decide(check = null, network = true))
        assertEquals(keepAll, decide(check = null, allowed = false))
        assertEquals(keepAll, decide(check = null, cached = null))
    }
}
