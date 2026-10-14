package com.reviewapp.android

import android.app.Application
import com.reviewapp.android.di.AppContainer
import kotlinx.coroutines.launch

class ReviewApplication : Application() {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
        container.appScope.launch { container.sessionManager.restore() }
    }
}
