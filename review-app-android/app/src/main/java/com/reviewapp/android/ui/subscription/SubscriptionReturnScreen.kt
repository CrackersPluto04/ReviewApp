package com.reviewapp.android.ui.subscription

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.WorkspacePremium
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.lifecycle.createSavedStateHandle
import androidx.navigation.toRoute
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.SubscriptionRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.ui.components.PremiumGold
import com.reviewapp.android.ui.navigation.SubscriptionReturnRoute
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

sealed interface ReturnUiState {
    data object Checking : ReturnUiState
    data object Success : ReturnUiState
    /** Paid, but the provider hasn't confirmed it yet. */
    data object Pending : ReturnUiState
    data object Cancelled : ReturnUiState
    /** Back from the payment settings. */
    data object PortalDone : ReturnUiState
    data object LoggedOut : ReturnUiState
    data class Error(val message: String) : ReturnUiState
}

/**
 * `reviewapp://subscription/{result}`, where the payment provider's pages send the user back (the web's
 * `SubscriptionReturnPage`). The link proves nothing: the API is asked to check with the provider.
 */
class SubscriptionReturnViewModel(
    savedStateHandle: SavedStateHandle,
    private val repository: SubscriptionRepository,
    private val sessionManager: SessionManager,
) : ViewModel() {

    private val result = CheckoutReturn.fromResult(savedStateHandle.toRoute<SubscriptionReturnRoute>().result)

    private val _state = MutableStateFlow<ReturnUiState>(ReturnUiState.Checking)
    val state: StateFlow<ReturnUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            // The app may have been restarted by the link: wait for the stored session
            val auth = sessionManager.state.first { it != AuthState.Loading }
            _state.value = when {
                result == CheckoutReturn.Cancelled -> ReturnUiState.Cancelled
                auth !is AuthState.LoggedIn -> ReturnUiState.LoggedOut
                result == CheckoutReturn.Portal -> {
                    // Payment details may have fixed a failed renewal
                    repository.awaitPremium(timeoutMs = 0)
                    ReturnUiState.PortalDone
                }
                else -> when (val status = repository.awaitPremium()) {
                    is ApiResult.Success -> if (status.data.isPremium) ReturnUiState.Success else ReturnUiState.Pending
                    is ApiResult.Error -> ReturnUiState.Error(status.message)
                }
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                SubscriptionReturnViewModel(createSavedStateHandle(), c.subscriptionRepository, c.sessionManager)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SubscriptionReturnScreen(
    onOpenPremium: () -> Unit,
    onHome: () -> Unit,
    onLogin: () -> Unit,
    viewModel: SubscriptionReturnViewModel = viewModel(factory = SubscriptionReturnViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    Scaffold(topBar = { TopAppBar(title = { Text("Premium") }) }) { padding ->
        Column(
            Modifier.fillMaxSize().padding(padding).padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        ) {
            Icon(
                Icons.Filled.WorkspacePremium,
                contentDescription = null,
                tint = if (state == ReturnUiState.Success) PremiumGold else MaterialTheme.colorScheme.outline,
                modifier = Modifier.size(64.dp),
            )
            val (title, message) = texts(state)
            Text(title, style = MaterialTheme.typography.headlineSmall, textAlign = TextAlign.Center)
            Text(message, style = MaterialTheme.typography.bodyLarge, textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant)

            when (state) {
                ReturnUiState.Checking -> CircularProgressIndicator()
                ReturnUiState.LoggedOut -> Button(onClick = onLogin) { Text("Log in") }
                else -> {
                    Button(onClick = onOpenPremium) {
                        Text(if (state == ReturnUiState.Cancelled) "Back to Premium" else "View my subscription")
                    }
                    TextButton(onClick = onHome) { Text("Go to the home page") }
                }
            }
        }
    }
}

private fun texts(state: ReturnUiState): Pair<String, String> = when (state) {
    ReturnUiState.Checking -> "Confirming your payment..." to "This only takes a moment."
    ReturnUiState.Success -> "Welcome to Premium!" to "Your Premium features are unlocked. Thanks for supporting the app!"
    ReturnUiState.Pending -> "Almost there" to
        "Your payment is still being processed. You'll get a notification once Premium is active."
    ReturnUiState.Cancelled -> "Checkout cancelled" to "Nothing was charged. You can subscribe whenever you like."
    ReturnUiState.PortalDone -> "All set" to "Your payment settings are up to date."
    ReturnUiState.LoggedOut -> "Your subscription" to "Log in to see your subscription."
    is ReturnUiState.Error -> "Your subscription" to state.message
}
