package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.RecognitionApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Recognition
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionItems
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import java.io.File

/**
 * Camera recognition (the web's `RecognitionService`). The suggested media are handed to [MediaRepository]'s cache,
 * so their details open without another request.
 */
class RecognitionRepository(
    private val api: RecognitionApi,
    private val mediaRepository: MediaRepository,
) {
    suspend fun allowance(): ApiResult<RecognitionAllowance> = apiCall { api.getAllowance() }.map { it.toDomain() }

    /** Cancelling the calling coroutine cancels the upload (the result is then discarded). */
    suspend fun recognize(photo: File): ApiResult<Recognition> {
        val part = MultipartBody.Part.createFormData("image", "photo.jpg", photo.asRequestBody("image/jpeg".toMediaType()))
        return apiCall { api.recognize(part) }.map { dto ->
            dto.toDomain().also { recognition -> mediaRepository.remember(recognition.firstPage.items.map { it.media }) }
        }
    }

    suspend fun items(recognitionId: String, offset: Int, count: Int? = null): ApiResult<RecognitionItems> =
        apiCall { api.getItems(recognitionId, offset, count) }.map { dto ->
            dto.toDomain().also { page -> mediaRepository.remember(page.items.map { it.media }) }
        }
}
