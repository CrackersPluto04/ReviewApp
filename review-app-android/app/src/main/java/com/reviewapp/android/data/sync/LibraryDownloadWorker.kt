package com.reviewapp.android.data.sync

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkInfo
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import com.reviewapp.android.ReviewApplication
import com.reviewapp.android.data.local.offline.SyncMetaKeys
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.domain.model.MediaType
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withTimeoutOrNull

/** The state of "Make available offline" for the settings screen. */
sealed interface LibraryDownloadState {
    data object Idle : LibraryDownloadState
    /** Enqueued, waiting for its network (Wi-Fi only) or for its turn. */
    data object Waiting : LibraryDownloadState
    data class Running(val done: Int, val total: Int) : LibraryDownloadState
}

/**
 * "Make my library available offline": refreshes the user's own content, then keeps the details page, the average
 * score and a small poster of every media in their reviews and collections. About one media a second, which keeps
 * the API's `external-api` limit (60 / min) free for the user. Resumable: whatever is already kept is skipped, so a
 * stopped run (Wi-Fi lost, app killed) continues where it was.
 */
class LibraryDownloadWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val c = (applicationContext as ReviewApplication).container
        // Started in a fresh process the session and the copy open first
        val mode = withTimeoutOrNull(30_000) { c.offlineGateway.status.first { it.mode.usesLocalCopy && it.mode !is OfflineMode.Draining } }?.mode
            ?: return Result.failure()
        val userId = mode.userId ?: return Result.failure()

        when (val synced = c.syncEngine.syncNow()) {
            is SyncOutcome.Failed -> return if (synced.isNetwork) Result.retry() else Result.failure()
            else -> Unit
        }
        val db = c.offlineGateway.database() ?: return Result.retry()
        val media = db.media().ownContentMedia()
        media.forEachIndexed { index, item ->
            if (isStopped) return Result.retry()
            setProgress(workDataOf(KEY_DONE to index, KEY_TOTAL to media.size))
            val type = MediaType.fromId(item.mediaType)
            val pageKey = SnapshotPolicy.keyOf("Media/media/${type.id}/${item.externalApiId}", null)
            if (pageKey != null && db.snapshots().get(pageKey) == null) {
                val details = c.mediaRepository.refreshForOffline(type, item.externalApiId)
                if (details is ApiResult.Error && details.isNetwork) return Result.retry()
                c.reviewRepository.statsForOffline(type, item.externalApiId)
                delay(PACE_MILLIS)
            }
            item.posterUrl?.let { c.offlineImages.download(userId, it) }
        }
        db.syncMeta().put(SyncMetaKeys.LIBRARY_DOWNLOADED_AT, System.currentTimeMillis().toString())
        return Result.success()
    }

    companion object {
        private const val NAME = "library-download"
        private const val KEY_DONE = "done"
        private const val KEY_TOTAL = "total"
        private const val PACE_MILLIS = 1_100L

        fun start(context: Context, wifiOnly: Boolean) {
            val request = OneTimeWorkRequestBuilder<LibraryDownloadWorker>()
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(if (wifiOnly) NetworkType.UNMETERED else NetworkType.CONNECTED)
                        .build(),
                )
                .build()
            // A new start replaces a waiting one (e.g. Wi-Fi only switched off), a running one continues anyway
            WorkManager.getInstance(context).enqueueUniqueWork(NAME, ExistingWorkPolicy.REPLACE, request)
        }

        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(NAME)
        }

        fun state(context: Context): Flow<LibraryDownloadState> =
            WorkManager.getInstance(context).getWorkInfosForUniqueWorkFlow(NAME).map { infos ->
                val info = infos.firstOrNull { !it.state.isFinished } ?: return@map LibraryDownloadState.Idle
                when (info.state) {
                    WorkInfo.State.RUNNING -> LibraryDownloadState.Running(
                        info.progress.getInt(KEY_DONE, 0),
                        info.progress.getInt(KEY_TOTAL, 0),
                    )
                    else -> LibraryDownloadState.Waiting
                }
            }
    }
}
