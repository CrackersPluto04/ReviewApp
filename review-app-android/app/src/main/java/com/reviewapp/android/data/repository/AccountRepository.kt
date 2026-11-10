package com.reviewapp.android.data.repository

import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.SecurityApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.ChangeEmailRequest
import com.reviewapp.android.data.remote.dto.ChangePasswordRequest
import com.reviewapp.android.data.remote.dto.CurrentPasswordRequest
import com.reviewapp.android.data.remote.dto.MessageResponse
import com.reviewapp.android.data.remote.dto.TwoFactorCodeRequest
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.SecurityStatus
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * The current user's account settings (the web's account dialogs). Successful calls return the
 * API's message to show.
 */
class AccountRepository(
    private val userApi: UserApi,
    private val securityApi: SecurityApi,
    private val authApi: AuthApi,
    private val sessionManager: SessionManager,
) {
    // In memory only, so the verification banner comes back on the next app start (like the web's sessionStorage)
    private val _verificationBannerDismissed = MutableStateFlow(false)

    /** Whether the user closed the email verification banner in this run of the app. */
    val verificationBannerDismissed: StateFlow<Boolean> = _verificationBannerDismissed.asStateFlow()

    /** Hides the banner until the app restarts. */
    fun dismissVerificationBanner() {
        _verificationBannerDismissed.value = true
    }

    /** Also updates the cached user's verified flag, so the verification banner follows it. */
    suspend fun status(): ApiResult<SecurityStatus> {
        val result = apiCall { securityApi.getStatus() }.map { it.toDomain() }
        if (result is ApiResult.Success) {
            val confirmed = result.data.isEmailConfirmed
            sessionManager.updateUser { if (it.isEmailConfirmed == confirmed) it else it.copy(isEmailConfirmed = confirmed) }
        }
        return result
    }

    /** Renames the user; the session and its cached copy follow the new name. Usernames are part of profile URLs, so the API applies its username rules. */
    suspend fun changeUsername(username: String): ApiResult<Unit> {
        val trimmed = username.trim()
        val result = apiCall { userApi.updateMe(UserUpdateRequest(username = trimmed)) }
        if (result is ApiResult.Success) sessionManager.updateUser { it.copy(username = trimmed) }
        return result.map { }
    }

    /** Requests an email change. The email only changes once the link sent to the new address is opened. */
    suspend fun changeEmail(newEmail: String, currentPassword: String): ApiResult<String> =
        apiCall { userApi.changeEmail(ChangeEmailRequest(newEmail.trim(), currentPassword)) }
            .message("Check your new inbox to confirm the change.")

    /** The response's new cookie keeps this device logged in, every other session is revoked. */
    suspend fun changePassword(currentPassword: String, newPassword: String): ApiResult<String> =
        apiCall { userApi.changePassword(ChangePasswordRequest(currentPassword, newPassword)) }
            .message("Password changed successfully.")

    /** Drops the pending email change. */
    suspend fun cancelPendingEmail(): ApiResult<Unit> = apiCall { securityApi.cancelPendingEmail() }

    /** Sends the verification email again; the API allows one per minute. */
    suspend fun resendVerification(): ApiResult<String> =
        apiCall { securityApi.resendVerification() }.message("We sent you a new verification link.")

    /** Emails a code for turning 2FA on ([enable]) or off; calling it again sends a new one. */
    suspend fun requestTwoFactor(enable: Boolean, currentPassword: String): ApiResult<String> {
        val body = CurrentPasswordRequest(currentPassword)
        return apiCall {
            if (enable) securityApi.requestEnableTwoFactor(body) else securityApi.requestDisableTwoFactor(body)
        }.message("We sent a 6-digit code to your email address.")
    }

    /** Confirms the emailed code, then reloads the user so the 2FA state is current. */
    suspend fun confirmTwoFactor(enable: Boolean, code: String): ApiResult<String> {
        val body = TwoFactorCodeRequest(code)
        val result = apiCall {
            if (enable) securityApi.confirmEnableTwoFactor(body) else securityApi.confirmDisableTwoFactor(body)
        }.message(if (enable) "Two-factor authentication is now enabled." else "Two-factor authentication is now disabled.")
        if (result is ApiResult.Success) sessionManager.refreshUser()
        return result
    }

    /** Revokes every session; this device is logged out locally as the API deleted its cookie. */
    suspend fun logoutAll(): ApiResult<String> {
        val result = apiCall { authApi.logoutAll() }.message("Logged out of all devices.")
        if (result is ApiResult.Success) sessionManager.onLoggedOutEverywhere()
        return result
    }

    /** Reloads the logged-in user from the API (see [SessionManager.refreshUser]). */
    suspend fun refreshUser() = sessionManager.refreshUser()

    private fun ApiResult<MessageResponse>.message(fallback: String): ApiResult<String> =
        map { it.message?.takeIf(String::isNotBlank) ?: fallback }
}
