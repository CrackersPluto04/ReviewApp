package com.reviewapp.android.data.sync

/** What the app can reach right now. */
enum class Connectivity {
    /** A validated network and a backend that answers. */
    Online,


    /** No network, or one without real internet (a captive portal: Android doesn't validate it). */
    Offline,
    /** A working network, but the backend doesn't answer. */
    ServerUnreachable,
}

/**
 * What the app knows about its connection: whether Android has a validated network, and whether the backend
 * answered the last request. Pure, so the rules are unit-tested.
 */
data class ConnectivityState(
    val networkValidated: Boolean,
    val serverReachable: Boolean = true,
) {
    /** The state the rest of the app uses. */
    val connectivity: Connectivity
        get() = when {
            !networkValidated -> Connectivity.Offline
            !serverReachable -> Connectivity.ServerUnreachable
            else -> Connectivity.Online
        }

    /**
     * A network change. A network that comes back is assumed to reach the backend (the next request or the
     * probe tells otherwise), so a backend failure seen offline doesn't stick.
     */
    fun onNetwork(validated: Boolean): ConnectivityState =
        if (validated && !networkValidated) ConnectivityState(networkValidated = true, serverReachable = true)
        else copy(networkValidated = validated)

    /** The backend answered (any HTTP status). */
    fun onServerResponded(): ConnectivityState = copy(serverReachable = true)

    /**
     * A request failed without an answer. Without a validated network that's just being offline; with one,
     * the backend is unreachable.
     */
    fun onServerUnreachable(): ConnectivityState = if (networkValidated) copy(serverReachable = false) else this
}
