package com.reviewapp.android.data.repository

import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.SubscriptionApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.MessageResponse
import com.reviewapp.android.data.remote.dto.SubscriptionRedirectRequest
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.SubscriptionSummary
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch

/**
 * The Premium subscription (the web's `SubscriptionService`). The API decides who is Premium; this keeps
 * the cached user's `isPremium` / entitlements in sync after every change.
 */
class SubscriptionRepository(
    private val api: SubscriptionApi,
    private val sessionManager: SessionManager,
    private val appScope: CoroutineScope,
) {
    // A locked feature was requested (402): the root shows the upsell
    private val _premiumRequired = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val premiumRequired: SharedFlow<Unit> = _premiumRequired.asSharedFlow()

    suspend fun plan(): ApiResult<Plan> = apiCall { api.getPlan() }.map { it.toDomain() }

    suspend fun status(): ApiResult<SubscriptionSummary> = syncUser(apiCall { api.getStatus() }.map { it.toDomain() })

    /** The checkout page to open in the browser. */
    suspend fun checkoutUrl(): ApiResult<String> = apiCall { api.startCheckout(SubscriptionRedirectRequest()) }.map { it.url }

    /** The payment provider's page for payment details and invoices. */
    suspend fun portalUrl(): ApiResult<String> = apiCall { api.openPortal(SubscriptionRedirectRequest()) }.map { it.url }

    suspend fun cancel(): ApiResult<String> = afterChange(apiCall { api.cancel() }.message("Your subscription is cancelled."))

    suspend fun resume(): ApiResult<String> = afterChange(apiCall { api.resume() }.message("Your subscription will renew again."))

    /** The placeholder Premium feature; a Free user gets a 402 here, which also opens the upsell. */
    suspend fun tryEntitlementPreview(): ApiResult<String> =
        apiCall { api.entitlementPreview() }.message("You have access to Premium features.")

    /**
     * After returning from checkout: asks the API to check with the provider, then polls the status until
     * Premium shows up or [timeoutMs] passes (the provider's notification can arrive a little later).
     * The redirect itself proves nothing.
     */
    suspend fun awaitPremium(timeoutMs: Long = 30_000, intervalMs: Long = 2_000): ApiResult<SubscriptionSummary> {
        var result = syncUser(apiCall { api.sync() }.map { it.toDomain() })
        var waited = 0L
        while (result is ApiResult.Success && !result.data.isPremium && waited < timeoutMs) {
            delay(intervalMs)
            waited += intervalMs
            result = status()
        }
        return result
    }

    /** Called by the HTTP client for every 402 response. */
    fun onPremiumRequired() {
        _premiumRequired.tryEmit(Unit)
        // It can also mean that the subscription just ended
        appScope.launch { sessionManager.refreshUser() }
    }

    private suspend fun syncUser(result: ApiResult<SubscriptionSummary>): ApiResult<SubscriptionSummary> {
        if (result is ApiResult.Success && sessionManager.currentUser?.isPremium != result.data.isPremium) {
            // Entitlements come with the user, so reload it instead of patching the flag
            sessionManager.refreshUser()
        }
        return result
    }

    private suspend fun afterChange(result: ApiResult<String>): ApiResult<String> {
        if (result is ApiResult.Success) sessionManager.refreshUser()
        return result
    }

    private fun ApiResult<MessageResponse>.message(fallback: String): ApiResult<String> =
        map { it.message?.takeIf(String::isNotBlank) ?: fallback }
}
