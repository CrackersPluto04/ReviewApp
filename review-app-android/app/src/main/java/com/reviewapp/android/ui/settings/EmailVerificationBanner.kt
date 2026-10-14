package com.reviewapp.android.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.outlined.MarkEmailUnread
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.AccountRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.ui.components.MessageBanner
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class VerificationBannerState(
    val visible: Boolean = false,
    val isSending: Boolean = false,
    val result: String? = null,
    val resultIsError: Boolean = false,
)

/**
 * The web's `EmailVerificationBanner`: shown to a logged-in user whose email isn't verified until
 * they dismiss it (for this app run, the flag lives in [AccountRepository]).
 */
class VerificationBannerViewModel(
    private val repository: AccountRepository,
    sessionManager: SessionManager,
) : ViewModel() {

    private val resend = MutableStateFlow(VerificationBannerState())

    val state: StateFlow<VerificationBannerState> = combine(
        sessionManager.state,
        repository.verificationBannerDismissed,
        resend,
    ) { auth, dismissed, local ->
        val unverified = (auth as? AuthState.LoggedIn)?.user?.isEmailConfirmed == false
        local.copy(visible = unverified && !dismissed)
    }.stateIn(viewModelScope, SharingStarted.Eagerly, VerificationBannerState())

    fun resend() {
        if (resend.value.isSending) return
        resend.update { it.copy(isSending = true) }
        viewModelScope.launch {
            val result = repository.resendVerification()
            resend.update {
                when (result) {
                    is ApiResult.Success -> it.copy(isSending = false, result = result.data + " Don't forget to check your spam folder.", resultIsError = false)
                    is ApiResult.Error -> it.copy(isSending = false, result = result.message, resultIsError = true)
                }
            }
        }
    }

    fun dismiss() = repository.dismissVerificationBanner()

    /** The link is opened in the browser, so coming back to the app checks whether it was verified. */
    fun refresh() {
        viewModelScope.launch { repository.refreshUser() }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                VerificationBannerViewModel(c.accountRepository, c.sessionManager)
            }
        }
    }
}

/** Only call it while `state.visible`, so lists don't get an empty item. */
@Composable
fun EmailVerificationBanner(viewModel: VerificationBannerViewModel, modifier: Modifier = Modifier) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    LifecycleResumeEffect(viewModel) {
        viewModel.refresh()
        onPauseOrDispose { }
    }
    Card(modifier.fillMaxWidth()) {
        Row(Modifier.padding(start = 16.dp, top = 12.dp)) {
            Icon(
                Icons.Outlined.MarkEmailUnread,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(top = 2.dp, end = 12.dp),
            )
            Column(Modifier.weight(1f)) {
                Text(
                    "Please verify your email address. We sent you a link when you registered (check your spam folder too).",
                    style = MaterialTheme.typography.bodyMedium,
                )
                state.result?.let { MessageBanner(it, isError = state.resultIsError, Modifier.padding(top = 8.dp)) }
                TextButton(onClick = viewModel::resend, enabled = !state.isSending) { Text("Resend email") }
            }
            IconButton(onClick = viewModel::dismiss) { Icon(Icons.Filled.Close, contentDescription = "Dismiss") }
        }
    }
}
