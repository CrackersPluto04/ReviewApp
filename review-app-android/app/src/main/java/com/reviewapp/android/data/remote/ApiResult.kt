package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.ErrorResponse
import kotlinx.coroutines.asContextElement
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import retrofit2.HttpException
import java.io.IOException

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
    ) : ApiResult<Nothing>
}

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
    ApiResult.Error(ApiErrors.message(e.code(), body), e.code(), ApiErrors.reason(body))
} catch (_: IOException) {
    ApiResult.Error(ApiErrors.NETWORK_ERROR, isNetwork = true)
} catch (_: SerializationException) {
    ApiResult.Error(ApiErrors.UNEXPECTED_RESPONSE)
}

object ApiErrors {
    const val NETWORK_ERROR = "Could not reach the server. Check your connection and try again."
    const val UNEXPECTED_RESPONSE = "Unexpected response from the server."

    private val json = Json { ignoreUnknownKeys = true }

    /**
     * Picks the message to show for a failed request: `{ error }` first, then the first
     * model-validation message, then a generic one for the status code.
     */
    /** The API's machine readable `code` of an error body, if any. */
    fun reason(body: String?): String? = parse(body)?.code?.takeIf { it.isNotBlank() }

    private fun parse(body: String?): ErrorResponse? = body?.takeIf { it.isNotBlank() }?.let {
        try {
            json.decodeFromString<ErrorResponse>(it)
        } catch (_: Exception) {
            null
        }
    }

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
