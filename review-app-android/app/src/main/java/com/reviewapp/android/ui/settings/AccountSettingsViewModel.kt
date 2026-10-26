package com.reviewapp.android.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.AccountRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.SecurityStatus
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** The open dialog of the settings screen with its fields. */
sealed interface SettingsDialog {
    data class Username(val form: UsernameForm) : SettingsDialog
    data class Email(val form: EmailForm) : SettingsDialog
    data class Password(val form: PasswordForm) : SettingsDialog
    data class TwoFactor(val flow: TwoFactorFlow) : SettingsDialog
    data object LogoutAll : SettingsDialog
}

data class AccountSettingsUiState(
    val username: String = "",
    /** From the cached user; the Premium screen loads the details. */
    val isPremium: Boolean = false,
    val status: SecurityStatus? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    val dialog: SettingsDialog? = null,
    /** A request of the open dialog (or of a button on the screen) is running. */
    val isSubmitting: Boolean = false,
    val dialogError: String? = null,
    /** Seconds until the verification email can be sent again (the API has a 60 s cooldown). */
    val verificationResendSecondsLeft: Int = 0,
    /** Seconds until a new 2FA code can be requested. */
    val codeResendSecondsLeft: Int = 0,
)

sealed interface AccountSettingsEvent {
    data class Message(val text: String) : AccountSettingsEvent
    /** Logged out (here or by a revoked session): the screen closes. */
    data object LoggedOut : AccountSettingsEvent
}

/**
 * The web's account settings menu and its dialogs on one screen: username, email, password, email
 * verification, the pending email change, two-factor authentication and the sessions.
 */
class AccountSettingsViewModel(
    private val repository: AccountRepository,
    private val sessionManager: SessionManager,
) : ViewModel() {

    private val _state = MutableStateFlow(AccountSettingsUiState())
    val state: StateFlow<AccountSettingsUiState> = _state.asStateFlow()

    private val _events = Channel<AccountSettingsEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    private var statusJob: Job? = null
    private var verificationCountdown: Job? = null
    private var codeCountdown: Job? = null

    init {
        viewModelScope.launch {
            sessionManager.state.collect { auth ->
                when (auth) {
                    is AuthState.LoggedIn -> _state.update { it.copy(username = auth.user.username, isPremium = auth.user.isPremium) }
                    AuthState.LoggedOut -> _events.send(AccountSettingsEvent.LoggedOut)
                    AuthState.Loading -> Unit
                }
            }
        }
        loadStatus()
    }

    /** Called on every resume: the email may have been verified or changed in the browser. */
    fun onResume() {
        if (_state.value.status != null) loadStatus(quiet = true)
    }

    fun loadStatus(quiet: Boolean = false) {
        statusJob?.cancel()
        if (!quiet) _state.update { it.copy(isLoading = true, error = null) }
        statusJob = viewModelScope.launch {
            when (val result = repository.status()) {
                is ApiResult.Success -> _state.update { it.copy(status = result.data, isLoading = false, error = null) }
                is ApiResult.Error -> if (!quiet) _state.update { it.copy(isLoading = false, error = result.message) }
            }
        }
    }

    // Dialogs

    fun dismissDialog() {
        if (_state.value.isSubmitting) return
        _state.update { it.copy(dialog = null, dialogError = null) }
    }

    fun openUsername() = openDialog(SettingsDialog.Username(UsernameForm(current = _state.value.username)))
    fun openEmail() = openDialog(SettingsDialog.Email(EmailForm()))
    fun openPassword() = openDialog(SettingsDialog.Password(PasswordForm()))
    fun openTwoFactor(enable: Boolean) = openDialog(SettingsDialog.TwoFactor(TwoFactorFlow(enable)))
    fun askLogoutAll() = openDialog(SettingsDialog.LogoutAll)

    private fun openDialog(dialog: SettingsDialog) = _state.update { it.copy(dialog = dialog, dialogError = null) }

    fun updateUsername(value: String) = updateDialog<SettingsDialog.Username> {
        it.copy(form = it.form.copy(value = value, serverError = null))
    }

    fun updateEmailForm(transform: (EmailForm) -> EmailForm) = updateDialog<SettingsDialog.Email> {
        it.copy(form = transform(it.form).copy(serverError = null))
    }

    fun updatePasswordForm(transform: (PasswordForm) -> PasswordForm) = updateDialog<SettingsDialog.Password> {
        it.copy(form = transform(it.form).copy(serverError = null))
    }

    fun updateTwoFactorPassword(value: String) = updateDialog<SettingsDialog.TwoFactor> { it.copy(flow = it.flow.withPassword(value)) }
    fun updateTwoFactorCode(value: String) = updateDialog<SettingsDialog.TwoFactor> { it.copy(flow = it.flow.withCode(value)) }

    private inline fun <reified T : SettingsDialog> updateDialog(crossinline transform: (T) -> SettingsDialog) =
        _state.update { state ->
            val dialog = state.dialog as? T ?: return@update state
            state.copy(dialog = transform(dialog))
        }

    // Account

    fun saveUsername() {
        val form = (_state.value.dialog as? SettingsDialog.Username)?.form ?: return
        if (!form.canSave || _state.value.isSubmitting) return
        submit(
            request = { repository.changeUsername(form.value) },
            onSuccess = { closeWith("Your username is now ${form.value.trim()}.") },
            onError = { message -> updateDialog<SettingsDialog.Username> { it.copy(form = it.form.copy(serverError = message)) } },
        )
    }

    fun saveEmail() {
        val form = (_state.value.dialog as? SettingsDialog.Email)?.form ?: return
        if (!form.canSave || _state.value.isSubmitting) return
        submit(
            request = { repository.changeEmail(form.newEmail, form.currentPassword) },
            onSuccess = { message ->
                closeWith(message)
                loadStatus(quiet = true)
            },
            // The password is retyped after a failure, like on the web
            onError = { message ->
                updateDialog<SettingsDialog.Email> { it.copy(form = it.form.copy(currentPassword = "", serverError = message)) }
            },
        )
    }

    fun savePassword() {
        val form = (_state.value.dialog as? SettingsDialog.Password)?.form ?: return
        if (!form.canSave || _state.value.isSubmitting) return
        submit(
            request = { repository.changePassword(form.currentPassword, form.newPassword) },
            onSuccess = { message -> closeWith(message) },
            onError = { message ->
                updateDialog<SettingsDialog.Password> { it.copy(form = it.form.copy(currentPassword = "", serverError = message)) }
            },
        )
    }

    // Security

    fun resendVerification() {
        if (_state.value.isSubmitting || _state.value.verificationResendSecondsLeft > 0) return
        submit(
            request = { repository.resendVerification() },
            onSuccess = { message ->
                _events.trySend(AccountSettingsEvent.Message(message))
                verificationCountdown = countdown(verificationCountdown) { s -> copy(verificationResendSecondsLeft = s) }
            },
            onError = { message -> _events.trySend(AccountSettingsEvent.Message(message)) },
        )
    }

    fun cancelPendingEmail() {
        if (_state.value.isSubmitting) return
        submit(
            request = { repository.cancelPendingEmail() },
            onSuccess = {
                _events.trySend(AccountSettingsEvent.Message("The pending email change was cancelled."))
                loadStatus(quiet = true)
            },
            onError = { message -> _events.trySend(AccountSettingsEvent.Message(message)) },
        )
    }

    /** The password step, and "Resend code" in the code step. */
    fun requestTwoFactorCode() {
        val flow = (_state.value.dialog as? SettingsDialog.TwoFactor)?.flow ?: return
        val waiting = flow.step == TwoFactorFlow.Step.Code && _state.value.codeResendSecondsLeft > 0
        if (!flow.canRequestCode || waiting) return
        updateDialog<SettingsDialog.TwoFactor> { it.copy(flow = it.flow.submitting()) }
        viewModelScope.launch {
            val result = repository.requestTwoFactor(flow.enable, flow.password)
            updateDialog<SettingsDialog.TwoFactor> {
                it.copy(
                    flow = when (result) {
                        is ApiResult.Success -> it.flow.codeSent(result.data)
                        is ApiResult.Error -> it.flow.failed(result.message)
                    },
                )
            }
            if (result is ApiResult.Success) codeCountdown = countdown(codeCountdown) { s -> copy(codeResendSecondsLeft = s) }
        }
    }

    fun confirmTwoFactor() {
        val flow = (_state.value.dialog as? SettingsDialog.TwoFactor)?.flow ?: return
        if (!flow.canConfirm) return
        updateDialog<SettingsDialog.TwoFactor> { it.copy(flow = it.flow.submitting()) }
        viewModelScope.launch {
            when (val result = repository.confirmTwoFactor(flow.enable, flow.code)) {
                is ApiResult.Success -> {
                    codeCountdown?.cancel()
                    _state.update { it.copy(codeResendSecondsLeft = 0) }
                    closeWith(result.data)
                    loadStatus(quiet = true)
                }
                is ApiResult.Error -> updateDialog<SettingsDialog.TwoFactor> { it.copy(flow = it.flow.failed(result.message)) }
            }
        }
    }

    // Sessions

    fun logoutAll() {
        if (_state.value.isSubmitting) return
        submit(
            request = { repository.logoutAll() },
            // The LoggedOut state closes the screen
            onSuccess = { message ->
                _state.update { it.copy(dialog = null) }
                _events.trySend(AccountSettingsEvent.Message(message))
            },
            onError = { message -> _state.update { it.copy(dialogError = message) } },
        )
    }

    fun logout() {
        viewModelScope.launch { sessionManager.logout() }
    }

    private fun <T> submit(
        request: suspend () -> ApiResult<T>,
        onSuccess: (T) -> Unit,
        onError: (String) -> Unit,
    ) {
        _state.update { it.copy(isSubmitting = true, dialogError = null) }
        viewModelScope.launch {
            val result = request()
            _state.update { it.copy(isSubmitting = false) }
            when (result) {
                is ApiResult.Success -> onSuccess(result.data)
                is ApiResult.Error -> onError(result.message)
            }
        }
    }

    private fun closeWith(message: String) {
        _state.update { it.copy(dialog = null, dialogError = null) }
        _events.trySend(AccountSettingsEvent.Message(message))
    }

    /** Counts [RESEND_COOLDOWN_SECONDS] down to 0 through [set]. */
    private fun countdown(previous: Job?, set: AccountSettingsUiState.(Int) -> AccountSettingsUiState): Job {
        previous?.cancel()
        return viewModelScope.launch {
            for (seconds in RESEND_COOLDOWN_SECONDS downTo 0) {
                _state.update { it.set(seconds) }
                if (seconds > 0) delay(1_000)
            }
        }
    }

    companion object {
        /** The API's resend cooldown; it doesn't report the time left, so the app counts it. */
        const val RESEND_COOLDOWN_SECONDS = 60

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                AccountSettingsViewModel(c.accountRepository, c.sessionManager)
            }
        }
    }
}
