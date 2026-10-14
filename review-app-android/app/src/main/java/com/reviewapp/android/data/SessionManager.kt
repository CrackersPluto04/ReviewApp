package com.reviewapp.android.data

import com.reviewapp.android.data.local.SecureCookieJar
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.domain.model.AuthUser
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
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
 */
class SessionManager(
    private val authApi: AuthApi,
    private val cookieJar: SecureCookieJar,
) {
    private val _state = MutableStateFlow<AuthState>(AuthState.Loading)
    val state: StateFlow<AuthState> = _state.asStateFlow()

    val currentUser: AuthUser? get() = (_state.value as? AuthState.LoggedIn)?.user

    /** Checks the stored cookie with the API. On a network error the cookie is kept for the next try. */
    suspend fun restore() {
        val hasSession = withContext(Dispatchers.IO) { cookieJar.hasSession() }
        if (!hasSession) {
            _state.value = AuthState.LoggedOut
            return
        }
        when (val result = apiCall { authApi.checkAuth() }) {
            is ApiResult.Success -> _state.value = AuthState.LoggedIn(result.data.toDomain())
            is ApiResult.Error -> {
                // 401: expired or revoked (logout-all, password change); 404: the user was deleted
                if (result.code == 401 || result.code == 404) {
                    withContext(Dispatchers.IO) { cookieJar.clear() }
                }
                _state.value = AuthState.LoggedOut
            }
        }
    }

    /** Retries [restore] when an earlier attempt failed offline but a session is still stored. */
    suspend fun restoreIfNeeded() {
        if (_state.value == AuthState.LoggedOut && withContext(Dispatchers.IO) { cookieJar.hasSession() }) {
            restore()
        }
    }

    fun onLoggedIn(user: AuthUser) {
        _state.value = AuthState.LoggedIn(user)
    }

    /** Keeps the cached user in sync after a profile change (e.g. a new avatar). */
    fun updateUser(transform: (AuthUser) -> AuthUser) {
        _state.update { state -> if (state is AuthState.LoggedIn) AuthState.LoggedIn(transform(state.user)) else state }
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
            _state.update { state ->
                if (state is AuthState.LoggedIn && state.user.id == current.id) AuthState.LoggedIn(user) else state
            }
        }
    }

    /** After `logout-all`: the API already revoked every session and deleted the cookie. */
    suspend fun onLoggedOutEverywhere() {
        withContext(Dispatchers.IO) { cookieJar.clear() }
        _state.value = AuthState.LoggedOut
    }

    /** Called for every 401 response (see `UnauthorizedInterceptor`), like the web app's `apiFetch`. */
    fun onUnauthorized() {
        if (_state.value is AuthState.LoggedIn) {
            cookieJar.clear()
            _state.value = AuthState.LoggedOut
        }
    }

    suspend fun logout() {
        // The API deletes the cookie, but it's cleared locally either way so logout always works offline
        apiCall { authApi.logout() }
        withContext(Dispatchers.IO) { cookieJar.clear() }
        _state.value = AuthState.LoggedOut
    }
}
