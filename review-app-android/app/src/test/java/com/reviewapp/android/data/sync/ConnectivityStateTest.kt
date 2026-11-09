package com.reviewapp.android.data.sync

import org.junit.Assert.assertEquals
import org.junit.Test

class ConnectivityStateTest {

    private val online = ConnectivityState(networkValidated = true)

    @Test
    fun noValidatedNetwork_IsOffline_EvenWhenTheBackendLastAnswered() {
        // A captive portal: a network without validation
        assertEquals(Connectivity.Offline, ConnectivityState(networkValidated = false).connectivity)
        assertEquals(Connectivity.Offline, online.onNetwork(validated = false).connectivity)
    }

    @Test
    fun aFailedRequestOnAWorkingNetwork_MeansTheBackendIsUnreachable_UntilItAnswers() {
        val down = online.onServerUnreachable()

        assertEquals(Connectivity.ServerUnreachable, down.connectivity)
        assertEquals(Connectivity.Online, down.onServerResponded().connectivity)
    }

    @Test
    fun aFailedRequestWhileOffline_IsJustOffline_AndDoesntStickWhenTheNetworkReturns() {
        val offline = online.onNetwork(validated = false).onServerUnreachable()

        assertEquals(Connectivity.Offline, offline.connectivity)
        assertEquals(Connectivity.Online, offline.onNetwork(validated = true).connectivity)
    }

    @Test
    fun aNetworkThatComesBack_IsAssumedToReachTheBackend() {
        val down = online.onServerUnreachable()

        assertEquals(Connectivity.Online, down.onNetwork(false).onNetwork(true).connectivity)
    }

    @Test
    fun theSameNetworkReportedAgain_KeepsTheBackendState() {
        val down = online.onServerUnreachable()

        assertEquals(Connectivity.ServerUnreachable, down.onNetwork(validated = true).connectivity)
    }
}
