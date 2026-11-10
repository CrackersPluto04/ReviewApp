package com.reviewapp.android.data.sync

import android.util.Log
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.SnapshotEntity
import com.reviewapp.android.data.remote.CallProbe
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Protocol
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import java.io.IOException
import java.util.concurrent.atomic.AtomicInteger

/**
 * Network first, the last answer when there is none: while the logged-in user's offline copy is open, every
 * successful GET of a kept path ([SnapshotPolicy]) is stored in its `snapshots` table, and a GET that fails without
 * an answer is answered from there (a 200 with [HEADER], the call's [CallProbe] learns the snapshot's age). A call
 * that refuses snapshots (own content, read from the database instead) neither stores nor gets one.
 *
 * Must be the outermost interceptor: the connectivity interceptor inside it still sees the real failure.
 */
class SnapshotInterceptor(
    private val database: () -> OfflineDatabase?,
    private val budgetBytes: () -> Long,
    private val apiBasePath: String,
    private val clock: () -> Long = System::currentTimeMillis,
) : Interceptor {

    private val writes = AtomicInteger()

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        val db = database()
        val probe = request.tag(CallProbe::class.java)
        val key = if (request.method == "GET" && db != null && probe?.allowSnapshot != false) keyOf(request.url) else null
        if (db == null || key == null) return chain.proceed(request)

        val response = try {
            chain.proceed(request)
        } catch (e: IOException) {
            if (chain.call().isCanceled()) throw e
            val snapshot = runCatching { db.snapshots().get(key) }.getOrNull() ?: throw e
            runCatching { db.snapshots().touch(key, clock()) }
            probe?.servedSnapshotAt = snapshot.savedAt
            return Response.Builder()
                .request(request)
                .protocol(Protocol.HTTP_1_1)
                .code(200)
                .message("OK")
                .header(HEADER, snapshot.savedAt.toString())
                .body(snapshot.json.toResponseBody(JSON))
                .build()
        }

        val body = response.body
        if (response.code != 200 || body == null || body.contentLength() > MAX_BYTES) return response
        val text = body.string()
        store(db, key, text)
        return response.newBuilder().body(text.toResponseBody(body.contentType())).build()
    }

    private fun keyOf(url: okhttp3.HttpUrl): String? {
        val path = url.encodedPath
        if (!path.startsWith(apiBasePath)) return null
        return SnapshotPolicy.keyOf(path.removePrefix(apiBasePath), url.encodedQuery)
    }

    private fun store(db: OfflineDatabase, key: String, text: String) {
        if (text.length > MAX_BYTES) return
        try {
            val content = SnapshotIndex.contentOf(key, text)
            val now = clock()
            db.snapshots().put(
                SnapshotEntity(key, text, savedAt = now, lastUsedAt = now, sizeBytes = text.length.toLong(),
                    reviewIds = content.reviewsCsv(), collectionIds = content.collectionsCsv()),
            )
            if (writes.incrementAndGet() % EVICT_EVERY == 0) evict(db)
        } catch (e: Exception) {
            // A full disk or a closed database must never break the request itself
            Log.w(TAG, "Could not store a snapshot", e)
        }
    }

    /** Deletes the least recently used snapshots until the kept ones fit the budget. Runs every few writes, and after a sync. */
    fun evict(db: OfflineDatabase) {
        val snapshots = db.snapshots()
        val keys = SnapshotEviction.keysToEvict(snapshots.usage(), budgetBytes())
        keys.chunked(500).forEach(snapshots::delete)
    }

    companion object {
        /** Set on a response made from a snapshot; its value is when the snapshot was saved (epoch millis). */
        const val HEADER = "X-Offline-Snapshot"
        private val JSON = "application/json; charset=utf-8".toMediaType()
        /** Bigger answers aren't kept (a page of media is a few tens of KB). */
        private const val MAX_BYTES = 1_000_000L
        private const val EVICT_EVERY = 20
        private const val TAG = "SnapshotInterceptor"
    }
}
