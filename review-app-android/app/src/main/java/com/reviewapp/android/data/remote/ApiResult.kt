package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.ErrorResponse
import kotlinx.serialization.SerializationException
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import retrofit2.HttpException
import java.io.IOException

sealed interface ApiResult<out T> {
    data class Success<T>(val data: T) : ApiResult<T>
    data class Error(val message: String, val code: Int? = null) : ApiResult<Nothing>
}

inline fun <T, R> ApiResult<T>.map(transform: (T) -> R): ApiResult<R> = when (this) {
    is ApiResult.Success -> ApiResult.Success(transform(data))
    is ApiResult.Error -> this
}

/** Runs a Retrofit call and turns every expected failure into [ApiResult.Error]. Cancellation is rethrown. */
suspend fun <T> apiCall(block: suspend () -> T): ApiResult<T> = try {
    ApiResult.Success(block())
} catch (e: HttpException) {
    ApiResult.Error(ApiErrors.message(e.code(), e.response()?.errorBody()?.string()), e.code())
} catch (_: IOException) {
    ApiResult.Error(ApiErrors.NETWORK_ERROR)
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
    fun message(code: Int, body: String?): String {
        val parsed = body?.takeIf { it.isNotBlank() }?.let {
            try {
                json.decodeFromString<ErrorResponse>(it)
            } catch (_: Exception) {
                null
            }
        }
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
