package com.reviewapp.android.ui.subscription

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.SubscriptionRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.SubscriptionSummary
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** The Premium screen: the plan, the user's subscription and the running action. */
data class PremiumUiState(
    /** The plan's price and features; public, so it loads for logged-out visitors too. */
    val plan: Plan? = null,
    val isLoggedIn: Boolean = false,
    /** Null while logged out or not loaded yet. */
    val subscription: SubscriptionSummary? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    /** A request of one of the buttons is running. */
    val isBusy: Boolean = false,
    /** The cancel confirmation is open. */
    val confirmCancel: Boolean = false,
)

/** One-off results of the Premium screen. */
sealed interface PremiumEvent {
    /** A payment provider page, opened in the browser. */
    data class OpenUrl(val url: String) : PremiumEvent
    /** A confirmation or failure for the snackbar. */
    data class Message(val text: String) : PremiumEvent

    /** A logged-out visitor tapped Subscribe; the screen opens the login. */
    data object LoginRequired : PremiumEvent
}

/** The plans page with the current user's subscription and its actions (the web's `PremiumPage`). */
class PremiumViewModel(
    private val repository: SubscriptionRepository,
    private val sessionManager: SessionManager,
) : ViewModel() {

    private val _state = MutableStateFlow(PremiumUiState())
    val state: StateFlow<PremiumUiState> = _state.asStateFlow()

    private val _events = Channel<PremiumEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    // Set when the browser was opened, so coming back checks with the provider (the user may have paid and returned by hand)
    private var awaitingReturn = false

    init {
        viewModelScope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    _state.update { it.copy(isLoggedIn = userId != null, subscription = null) }
                    if (userId != null) loadStatus()
                }
        }
        loadPlan()
    }

    /** Loads the public plan (also the error retry). */
    fun loadPlan() {
        _state.update { it.copy(isLoading = true, error = null) }
        viewModelScope.launch {
            when (val result = repository.plan()) {
                is ApiResult.Success -> _state.update { it.copy(plan = result.data, isLoading = false) }
                is ApiResult.Error -> _state.update { it.copy(isLoading = false, error = result.message) }
            }
        }
    }

    /** Back from the browser (or the app was just reopened). */
    fun onResume() {
        if (!_state.value.isLoggedIn) return
        if (awaitingReturn) {
            awaitingReturn = false
            viewModelScope.launch {
                // One check with the provider, no waiting: the return screen polls, this only catches a manual switch back
                val result = repository.awaitPremium(timeoutMs = 0)
                if (result is ApiResult.Success) _state.update { it.copy(subscription = result.data) }
            }
        } else if (_state.value.subscription != null) {
            loadStatus()
        }
    }

    private fun loadStatus() {
        viewModelScope.launch {
            val result = repository.status()
            if (result is ApiResult.Success) _state.update { it.copy(subscription = result.data) }
        }
    }

    /** Opens the provider's checkout page in the browser; the provider returns through the `reviewapp://` deep link. */
    fun subscribe() {
        if (!_state.value.isLoggedIn) {
            _events.trySend(PremiumEvent.LoginRequired)
            return
        }
        openProviderPage { repository.checkoutUrl() }
    }

    /** Opens the provider's customer portal (payment details, invoices) in the browser. */
    fun managePayment() = openProviderPage { repository.portalUrl() }

    /** Opens the cancel confirmation. */
    fun askCancel() = _state.update { it.copy(confirmCancel = true) }

    fun dismissCancel() {
        if (!_state.value.isBusy) _state.update { it.copy(confirmCancel = false) }
    }

    /** Stops renewing; Premium stays until the paid period ends. */
    fun confirmCancel() = change { repository.cancel() }

    /** Takes back a cancellation that has not taken effect yet. */
    fun resume() = change { repository.resume() }

    /** Asks the API for a provider page URL and hands it to the screen to open in the browser. */
    private fun openProviderPage(request: suspend () -> ApiResult<String>) {
        if (_state.value.isBusy) return
        _state.update { it.copy(isBusy = true) }
        viewModelScope.launch {
            val result = request()
            _state.update { it.copy(isBusy = false) }
            when (result) {
                is ApiResult.Success -> {
                    awaitingReturn = true
                    _events.send(PremiumEvent.OpenUrl(result.data))
                }
                is ApiResult.Error -> _events.send(PremiumEvent.Message(result.message))
            }
        }
    }

    /** Runs a subscription change (cancel / resume), shows its message and reloads the status. */
    private fun change(request: suspend () -> ApiResult<String>) {
        if (_state.value.isBusy) return
        _state.update { it.copy(isBusy = true) }
        viewModelScope.launch {
            val result = request()
            _state.update { it.copy(isBusy = false, confirmCancel = false) }
            when (result) {
                is ApiResult.Success -> {
                    _events.send(PremiumEvent.Message(result.data))
                    loadStatus()
                }
                is ApiResult.Error -> _events.send(PremiumEvent.Message(result.message))
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                PremiumViewModel(c.subscriptionRepository, c.sessionManager)
            }
        }
    }
}
