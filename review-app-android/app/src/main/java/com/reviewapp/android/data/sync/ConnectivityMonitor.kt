package com.reviewapp.android.data.sync

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * The app's connectivity ([Connectivity]): Android's default network (validated = real internet, so a captive
 * portal counts as offline) plus what the API requests themselves tell (see `ConnectivityInterceptor`). While
 * the backend is unreachable on a working network, [probe] asks it every [PROBE_INTERVAL_MILLIS].
 */
class ConnectivityMonitor(
    context: Context,
    private val scope: CoroutineScope,
    /** True when the backend answered at all (any status). */
    private val probe: suspend () -> Boolean,
) {
    private val connectivityManager = context.getSystemService(ConnectivityManager::class.java)

    private val _state = MutableStateFlow(ConnectivityState(networkValidated = currentlyValidated()))

    /** The raw inputs (validated network, backend answering). */
    val state: StateFlow<ConnectivityState> = _state.asStateFlow()

    /** The derived state the app reacts to; only emits when it changes. */
    val connectivity: StateFlow<Connectivity> = _state.map { it.connectivity }.distinctUntilChanged()
        .stateIn(scope, SharingStarted.Eagerly, _state.value.connectivity)

    private var probeJob: Job? = null

    /** Starts listening to Android's default network. Called once by the application. */
    fun start() {
        connectivityManager.registerDefaultNetworkCallback(object : ConnectivityManager.NetworkCallback() {
            override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
                onNetwork(capabilities.isValidated())
            }

            override fun onLost(network: Network) = onNetwork(false)
        })
    }

    /** A validated network exists right now (read synchronously, for the app start decision). */
    fun isNetworkAvailable(): Boolean = _state.value.networkValidated

    /** Reported by the OkHttp interceptor for every response. The common case (already reachable) doesn't touch the flow. */
    fun onServerResponded() {
        if (!_state.value.serverReachable) reduce { it.onServerResponded() }
    }

    /** Reported by the OkHttp interceptor when a request got no answer; starts the probing. */
    fun onServerUnreachable() = reduce { it.onServerUnreachable() }

    private fun onNetwork(validated: Boolean) = reduce { it.onNetwork(validated) }

    private fun reduce(transform: (ConnectivityState) -> ConnectivityState) {
        _state.update(transform)
        if (_state.value.connectivity == Connectivity.ServerUnreachable) startProbing()
    }

    private fun startProbing() {
        if (probeJob?.isActive == true) return
        probeJob = scope.launch {
            while (isActive && _state.value.connectivity == Connectivity.ServerUnreachable) {
                delay(PROBE_INTERVAL_MILLIS)
                if (probe()) _state.update { it.onServerResponded() }
            }
        }
    }

    private fun currentlyValidated(): Boolean =
        connectivityManager.activeNetwork?.let { connectivityManager.getNetworkCapabilities(it) }?.isValidated() == true

    private fun NetworkCapabilities.isValidated() =
        hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) && hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)

    companion object {
        /** How often an unreachable backend is asked again. */
        const val PROBE_INTERVAL_MILLIS = 30_000L
    }
}
