package com.reviewapp.android.data.repository

import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.LoginRequest
import com.reviewapp.android.data.remote.dto.RegisterRequest
import com.reviewapp.android.data.remote.dto.TwoFactorLoginRequest
import com.reviewapp.android.data.remote.dto.TwoFactorResendRequest

sealed interface LoginOutcome {
    data object LoggedIn : LoginOutcome
    data class TwoFactorRequired(val challengeToken: String, val message: String) : LoginOutcome
}

class AuthRepository(
    private val authApi: AuthApi,
    private val sessionManager: SessionManager,
) {
    suspend fun register(username: String, email: String, password: String): ApiResult<String> =
        when (val result = apiCall { authApi.register(RegisterRequest(username.trim(), email.trim(), password)) }) {
            is ApiResult.Success -> ApiResult.Success(result.data.message ?: "Registration successful.")
            is ApiResult.Error -> result
        }

    suspend fun login(email: String, password: String): ApiResult<LoginOutcome> =
        when (val result = apiCall { authApi.login(LoginRequest(email.trim(), password)) }) {
            is ApiResult.Error -> result
            is ApiResult.Success -> {
                val body = result.data
                val user = body.user
                when {
                    body.twoFactorRequired && body.challengeToken != null -> ApiResult.Success(
                        LoginOutcome.TwoFactorRequired(
                            body.challengeToken,
                            body.message ?: "We sent a 6-digit code to your email address.",
                        ),
                    )
                    user != null -> {
                        sessionManager.onLoggedIn(user.toDomain())
                        ApiResult.Success(LoginOutcome.LoggedIn)
                    }
                    else -> ApiResult.Error("Unexpected response from the server.")
                }
            }
        }

    suspend fun loginTwoFactor(challengeToken: String, code: String): ApiResult<Unit> =
        when (val result = apiCall { authApi.loginTwoFactor(TwoFactorLoginRequest(challengeToken, code)) }) {
            is ApiResult.Error -> result
            is ApiResult.Success -> {
                val user = result.data.user ?: return ApiResult.Error("Unexpected response from the server.")
                sessionManager.onLoggedIn(user.toDomain())
                ApiResult.Success(Unit)
            }
        }

    suspend fun resendTwoFactorCode(challengeToken: String): ApiResult<String> =
        when (val result = apiCall { authApi.resendTwoFactorCode(TwoFactorResendRequest(challengeToken)) }) {
            is ApiResult.Success -> ApiResult.Success(result.data.message ?: "We sent you a new code.")
            is ApiResult.Error -> result
        }
}
