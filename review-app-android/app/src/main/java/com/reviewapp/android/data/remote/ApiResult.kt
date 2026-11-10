package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.ErrorResponse
import kotlinx.coroutines.asContextElement
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import retrofit2.HttpException
import java.io.IOException

/** The outcome of an API call: the data, or a failure that is already turned into a message. Calls never throw. */
sealed interface ApiResult<out T> {
    /**
     * [cachedAt]: the data is the device's offline copy from that time (epoch millis), not a fresh answer (see
     * `SnapshotInterceptor` and `LocalLibrary`).
     */
    data class Success<T>(val data: T, val cachedAt: Long? = null) : ApiResult<T>
    /**
     * [code] is the HTTP status, [reason] the API's machine readable `code` (e.g. `daily_limit`) when it sent one.
     * [isNetwork]: no answer at all (offline, timeout, backend down), so the offline copy may stand in.
     */
    data class Error(
        val message: String,
        val code: Int? = null,
        val reason: String? = null,
        val isNetwork: Boolean = false,
        /** The `Retry-After` of a 429 (or 503), in seconds. */
        val retryAfterSeconds: Long? = null,
    ) : ApiResult<Nothing>
}

/** Transforms the data of a success and keeps an error as it is. */
inline fun <T, R> ApiResult<T>.map(transform: (T) -> R): ApiResult<R> = when (this) {
    is ApiResult.Success -> ApiResult.Success(transform(data), cachedAt)
    is ApiResult.Error -> this
}

/**
 * Runs a Retrofit call and turns every expected failure into [ApiResult.Error]. Cancellation is rethrown.
 *
 * With offline mode a GET that gets no answer may be answered from its last snapshot (then [ApiResult.Success.cachedAt]
 * is set); [useSnapshot] false refuses that, for the user's own content, which the caller reads from the offline
 * database instead (complete, and it will include changes made offline).
 */
suspend fun <T> apiCall(useSnapshot: Boolean = true, block: suspend () -> T): ApiResult<T> = try {
    val probe = CallProbe(allowSnapshot = useSnapshot)
    val data = withContext(CallProbe.current.asContextElement(probe)) { block() }
    ApiResult.Success(data, probe.servedSnapshotAt)
} catch (e: HttpException) {
    val body = e.response()?.errorBody()?.string()
    val retryAfter = e.response()?.headers()?.get("Retry-After")?.trim()?.toLongOrNull()
    ApiResult.Error(ApiErrors.message(e.code(), body), e.code(), ApiErrors.reason(body), retryAfterSeconds = retryAfter)
} catch (_: IOException) {
    ApiResult.Error(ApiErrors.NETWORK_ERROR, isNetwork = true)
} catch (_: SerializationException) {
    ApiResult.Error(ApiErrors.UNEXPECTED_RESPONSE)
}

/** Turns failed responses into the text shown to the user. */
object ApiErrors {
    /** Shown when the request got no answer at all. */
    const val NETWORK_ERROR = "Could not reach the server. Check your connection and try again."
    /** Shown when the answer could not be read as the expected JSON. */
    const val UNEXPECTED_RESPONSE = "Unexpected response from the server."

    private val json = Json { ignoreUnknownKeys = true }

    /** The API's machine readable `code` of an error body (e.g. `daily_limit`), if any. */
    fun reason(body: String?): String? = parse(body)?.code?.takeIf { it.isNotBlank() }

    private fun parse(body: String?): ErrorResponse? = body?.takeIf { it.isNotBlank() }?.let {
        try {
            json.decodeFromString<ErrorResponse>(it)
        } catch (_: Exception) {
            null
        }
    }

    /**
     * Picks the message to show for a failed request: `{ error }` first, then the first
     * model-validation message, then a generic one for the status code.
     */
    fun message(code: Int, body: String?): String {
        val parsed = parse(body)
        parsed?.error?.takeIf { it.isNotBlank() }?.let { return it }
        parsed?.errors?.values?.flatten()?.firstOrNull { it.isNotBlank() }?.let { return it }

        return when (code) {
            401 -> "Your session has expired. Please log in again."
            402 -> "This is a Premium feature."
            403 -> "You don't have permission to do that."
            404 -> "Not found."
            429 -> "Too many requests. Please try again later."
            in 500..599 -> "Something went wrong on the server. Please try again later."
            else -> parsed?.title ?: "Request failed ($code)."
        }
    }
}
