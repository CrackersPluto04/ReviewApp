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

data class PremiumUiState(
    val plan: Plan? = null,
    val isLoggedIn: Boolean = false,
    /** Null while logged out or not loaded yet. */
    val subscription: SubscriptionSummary? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    /** A request of one of the buttons is running. */
    val isBusy: Boolean = false,
    val confirmCancel: Boolean = false,
)

sealed interface PremiumEvent {
    /** A payment provider page, opened in the browser. */
    data class OpenUrl(val url: String) : PremiumEvent
    data class Message(val text: String) : PremiumEvent
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

    // Set when the browser was opened, so coming back checks with the provider
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

    fun subscribe() {
        if (!_state.value.isLoggedIn) {
            _events.trySend(PremiumEvent.LoginRequired)
            return
        }
        openProviderPage { repository.checkoutUrl() }
    }

    fun managePayment() = openProviderPage { repository.portalUrl() }

    fun askCancel() = _state.update { it.copy(confirmCancel = true) }

    fun dismissCancel() {
        if (!_state.value.isBusy) _state.update { it.copy(confirmCancel = false) }
    }

    fun confirmCancel() = change { repository.cancel() }

    fun resume() = change { repository.resume() }

    /** The placeholder Premium feature; for a Free user the 402 opens the upsell by itself. */
    fun tryPreview() {
        viewModelScope.launch {
            when (val result = repository.tryEntitlementPreview()) {
                is ApiResult.Success -> _events.send(PremiumEvent.Message(result.data))
                is ApiResult.Error -> if (result.code != 402) _events.send(PremiumEvent.Message(result.message))
            }
        }
    }

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
