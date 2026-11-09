package com.reviewapp.android

import android.app.Activity
import android.app.Application
import android.os.Bundle
import coil.ImageLoader
import coil.ImageLoaderFactory
import coil.disk.DiskCache
import com.reviewapp.android.data.local.offline.OfflineImageInterceptor
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.SyncOutcome
import com.reviewapp.android.di.AppContainer
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.merge
import kotlinx.coroutines.launch

class ReviewApplication : Application(), ImageLoaderFactory {

    /**
     * Coil's loader for the whole app: the disk cache is sized by the offline cache budget (its image share; a
     * changed budget applies from the next start) and ignores the servers' cache headers, so images seen once also
     * show offline. Offline, the user's own posters load from their kept copies ([OfflineImageInterceptor]).
     */
    override fun newImageLoader(): ImageLoader {
        val c = container
        val budget = runBlocking { c.settingsStore.cacheBudget.first() }
        return ImageLoader.Builder(this)
            .diskCache { DiskCache.Builder().directory(cacheDir.resolve("image_cache")).maxSizeBytes(budget.imageBytes).build() }
            .respectCacheHeaders(false)
            .components {
                add(
                    OfflineImageInterceptor(
                        store = c.offlineImages,
                        userId = { c.sessionManager.currentUser?.id },
                        isOffline = { c.connectivityMonitor.connectivity.value != Connectivity.Online },
                    ),
                )
            }
            .build()
    }


    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
        val c = container
        c.connectivityMonitor.start()
        c.offlineGateway.start()
        // The user's own changes change their content on the backend: the offline copy follows (the events a sync
        // itself causes don't count, or they would start the next one)
        c.syncEngine.start(
            merge(
                c.reviewRepository.changes.filter { !it.fromSync }.map { },
                c.collectionRepository.changes.filter { !it.fromSync }.map { },
                c.userRepository.followChanges,
            ),
        )
        // Sent changes got their real ids: the lists and the hearts reload from the copy
        c.appScope.launch {
            c.syncEngine.pushed.collect {
                c.reviewRepository.onSynced()
                c.collectionRepository.onSynced()
            }
        }
        // Opening the app syncs, and shows what a background push unlocked meanwhile
        registerActivityLifecycleCallbacks(object : ActivityLifecycleCallbacks {
            private var started = 0
            override fun onActivityStarted(activity: Activity) {
                if (started++ == 0) {
                    c.isInForeground = true
                    c.syncEngine.requestSync()
                    c.appScope.launch { c.showPendingUnlocks() }
                }
            }
            override fun onActivityStopped(activity: Activity) {
                if (--started == 0) c.isInForeground = false
            }
            override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) = Unit
            override fun onActivityResumed(activity: Activity) = Unit
            override fun onActivityPaused(activity: Activity) = Unit
            override fun onActivitySaveInstanceState(activity: Activity, outState: Bundle) = Unit
            override fun onActivityDestroyed(activity: Activity) = Unit
        })
        c.appScope.launch { c.sessionManager.restore() }
        // Back online: retry a start that failed offline, verify a session opened offline
        c.appScope.launch {
            c.connectivityMonitor.connectivity
                .drop(1)
                .filter { it == Connectivity.Online }
                .collect { c.sessionManager.restoreIfNeeded() }
        }
        // The hearts and checkmarks load from the offline copy once it exists (e.g. a first start offline)
        c.appScope.launch {
            c.syncEngine.lastOutcome.filter { it == SyncOutcome.Done }.collect { c.collectionRepository.refreshIfNeeded() }
        }
    }
}
