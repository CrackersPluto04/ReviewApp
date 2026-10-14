package com.reviewapp.android.ui.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.AuthRepository
import com.reviewapp.android.data.repository.LoginOutcome
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.util.Validation
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class AuthMode { Login, Register, TwoFactor }

enum class AuthField { Username, Email, Password, ConfirmPassword, Code }

data class LoginUiState(
    val mode: AuthMode = AuthMode.Login,
    val username: String = "",
    val email: String = "",
    val password: String = "",
    val confirmPassword: String = "",
    val code: String = "",
    val fieldErrors: Map<AuthField, String> = emptyMap(),
    val error: String? = null,
    val info: String? = null,
    val isSubmitting: Boolean = false,
    val resendSecondsLeft: Int = 0,
)

class LoginViewModel(private val authRepository: AuthRepository) : ViewModel() {

    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state.asStateFlow()

    /** Fires once after a successful login so the screen can navigate back. */
    private val _loggedIn = Channel<Unit>(Channel.BUFFERED)
    val loggedIn = _loggedIn.receiveAsFlow()

    // Kept out of the UI state, it's only sent back to the API
    private var challengeToken: String? = null
    private var cooldownJob: Job? = null

    fun onUsernameChange(value: String) = updateField(AuthField.Username) { copy(username = value) }
    fun onEmailChange(value: String) = updateField(AuthField.Email) { copy(email = value) }
    fun onPasswordChange(value: String) = updateField(AuthField.Password) { copy(password = value) }
    fun onConfirmPasswordChange(value: String) = updateField(AuthField.ConfirmPassword) { copy(confirmPassword = value) }

    fun onCodeChange(value: String) {
        val digits = value.filter(Char::isDigit).take(Validation.TWO_FACTOR_CODE_LENGTH)
        updateField(AuthField.Code) { copy(code = digits) }
    }

    private fun updateField(field: AuthField, change: LoginUiState.() -> LoginUiState) {
        _state.update { it.change().copy(fieldErrors = it.fieldErrors - field, error = null) }
    }

    fun switchMode(mode: AuthMode) {
        if (mode == AuthMode.TwoFactor) return
        _state.update {
            it.copy(mode = mode, password = "", confirmPassword = "", fieldErrors = emptyMap(), error = null, info = null)
        }
    }

    fun submit() {
        val current = _state.value
        if (current.isSubmitting) return
        when (current.mode) {
            AuthMode.Login -> login(current)
            AuthMode.Register -> register(current)
            AuthMode.TwoFactor -> verifyCode(current)
        }
    }

    private fun login(s: LoginUiState) {
        val errors = buildMap {
            Validation.emailError(s.email)?.let { put(AuthField.Email, it) }
            Validation.loginPasswordError(s.password)?.let { put(AuthField.Password, it) }
        }
        if (errors.isNotEmpty()) return _state.update { it.copy(fieldErrors = errors) }

        submitting {
            when (val result = authRepository.login(s.email, s.password)) {
                is ApiResult.Error -> _state.update { it.copy(error = result.message) }
                is ApiResult.Success -> when (val outcome = result.data) {
                    LoginOutcome.LoggedIn -> _loggedIn.send(Unit)
                    is LoginOutcome.TwoFactorRequired -> {
                        challengeToken = outcome.challengeToken
                        _state.update {
                            it.copy(mode = AuthMode.TwoFactor, code = "", info = outcome.message, password = "")
                        }
                        startResendCooldown()
                    }
                }
            }
        }
    }

    private fun register(s: LoginUiState) {
        val errors = buildMap {
            Validation.usernameError(s.username)?.let { put(AuthField.Username, it) }
            Validation.emailError(s.email)?.let { put(AuthField.Email, it) }
            Validation.newPasswordError(s.password)?.let { put(AuthField.Password, it) }
            Validation.confirmPasswordError(s.password, s.confirmPassword)?.let { put(AuthField.ConfirmPassword, it) }
        }
        if (errors.isNotEmpty()) return _state.update { it.copy(fieldErrors = errors) }

        submitting {
            when (val result = authRepository.register(s.username, s.email, s.password)) {
                is ApiResult.Error -> _state.update { it.copy(error = result.message) }
                // Same as the web app: back to the login form with the email filled in
                is ApiResult.Success -> _state.update {
                    it.copy(
                        mode = AuthMode.Login,
                        password = "",
                        confirmPassword = "",
                        info = result.data,
                    )
                }
            }
        }
    }

    private fun verifyCode(s: LoginUiState) {
        val token = challengeToken ?: return backToLogin()
        if (!Validation.isValidTwoFactorCode(s.code)) {
            return _state.update { it.copy(fieldErrors = mapOf(AuthField.Code to "Enter the 6-digit code.")) }
        }
        submitting {
            when (val result = authRepository.loginTwoFactor(token, s.code)) {
                is ApiResult.Error -> _state.update { it.copy(error = result.message, code = "") }
                is ApiResult.Success -> _loggedIn.send(Unit)
            }
        }
    }

    fun resendCode() {
        val token = challengeToken ?: return
        if (_state.value.resendSecondsLeft > 0 || _state.value.isSubmitting) return
        submitting {
            when (val result = authRepository.resendTwoFactorCode(token)) {
                is ApiResult.Error -> _state.update { it.copy(error = result.message) }
                is ApiResult.Success -> {
                    _state.update { it.copy(info = result.data) }
                    startResendCooldown()
                }
            }
        }
    }

    fun backToLogin() {
        challengeToken = null
        cooldownJob?.cancel()
        _state.update {
            it.copy(mode = AuthMode.Login, code = "", fieldErrors = emptyMap(), error = null, info = null, resendSecondsLeft = 0)
        }
    }

    private fun startResendCooldown() {
        cooldownJob?.cancel()
        cooldownJob = viewModelScope.launch {
            for (seconds in RESEND_COOLDOWN_SECONDS downTo 0) {
                _state.update { it.copy(resendSecondsLeft = seconds) }
                if (seconds > 0) delay(1000)
            }
        }
    }

    private fun submitting(block: suspend () -> Unit) {
        _state.update { it.copy(isSubmitting = true, error = null) }
        viewModelScope.launch {
            try {
                block()
            } finally {
                _state.update { it.copy(isSubmitting = false) }
            }
        }
    }

    companion object {
        /** Matches the API's resend cooldown. */
        const val RESEND_COOLDOWN_SECONDS = 60

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer { LoginViewModel(appContainer().authRepository) }
        }
    }
}
