package com.reviewapp.android.data.sync

import android.content.Context
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.reviewapp.android.ReviewApplication
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.TimeUnit

/**
 * Sends the queued changes when the app isn't open (the spec's small background sync): one OS job, waiting for a
 * connection, enqueued whenever a change is queued. It only pushes, for the user whose copy is stored: no pull, no
 * banners (unlocks wait in `pending_unlocks` for the next app open), and no login attempt (a 401 leaves everything
 * queued). The app in the foreground does the same through [SyncEngine], one run at a time.
 */
class SyncPushWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val c = (applicationContext as ReviewApplication).container
        // Started in a fresh process: the session is restored and the copy opened first (none = nothing to send)
        withTimeoutOrNull(30_000) { c.offlineGateway.status.first { it.mode.usesLocalCopy } } ?: return Result.success()
        return when (val outcome = c.syncEngine.pushOnly()) {
            null, PushOutcome.Offline, is PushOutcome.Paused, is PushOutcome.Failed -> Result.retry()
            // Logged out or expired: the app asks for a login and sends the queue afterwards
            PushOutcome.NeedsLogin -> Result.success()
            // Some may wait for their retry time: come back later for them
            is PushOutcome.Done -> if ((c.offlineGateway.database()?.outbox()?.countToSend() ?: 0) > 0) Result.retry() else Result.success()
        }
    }

    companion object {
        private const val NAME = "sync-push"

        fun schedule(context: Context) {
            val request = OneTimeWorkRequestBuilder<SyncPushWorker>()
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
                .build()
            // One waiting job is enough: it sends whatever is queued when it runs
            WorkManager.getInstance(context).enqueueUniqueWork(NAME, ExistingWorkPolicy.KEEP, request)
        }
    }
}
