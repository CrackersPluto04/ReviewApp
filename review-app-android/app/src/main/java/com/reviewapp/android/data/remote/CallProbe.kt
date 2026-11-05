package com.reviewapp.android.data.remote

import okhttp3.Call
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * Travels with one [apiCall] to the OkHttp interceptors as a request tag: whether the call accepts an offline
 * snapshot as its answer, and when the snapshot it got was saved.
 *
 * It gets there through a thread local: [apiCall] sets it for its coroutine, and Retrofit creates the OkHttp call
 * ([ProbingCallFactory.newCall]) synchronously on the calling thread before the request is sent.
 */
class CallProbe(val allowSnapshot: Boolean) {
    @Volatile
    var servedSnapshotAt: Long? = null

    companion object {
        val current = ThreadLocal<CallProbe?>()
    }
}

/** Retrofit's call factory: tags every request with the calling [apiCall]'s [CallProbe]. */
class ProbingCallFactory(private val client: OkHttpClient) : Call.Factory {
    override fun newCall(request: Request): Call {
        val probe = CallProbe.current.get() ?: return client.newCall(request)
        return client.newCall(request.newBuilder().tag(CallProbe::class.java, probe).build())
    }
}
