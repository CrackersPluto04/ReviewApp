package com.reviewapp.android

import android.app.Application
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.di.AppContainer
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

class ReviewApplication : Application() {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
        container.connectivityMonitor.start()
        container.offlineGateway.start()
        container.appScope.launch { container.sessionManager.restore() }
        // Back online: retry a start that failed offline, verify a session opened offline
        container.appScope.launch {
            container.connectivityMonitor.connectivity
                .drop(1)
                .filter { it == Connectivity.Online }
                .collect { container.sessionManager.restoreIfNeeded() }
        }
    }
}
