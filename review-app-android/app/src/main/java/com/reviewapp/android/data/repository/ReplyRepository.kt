package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.ReviewReplyApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.CreateReplyRequest
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReplyContext
import com.reviewapp.android.domain.model.ReplyPage
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

/** A review's reply count changed by [delta] (the count covers both reply layers). */
data class ReplyCountChange(val reviewId: Int, val delta: Int)

class ReplyRepository(private val replyApi: ReviewReplyApi) {

    // Review lists patch their cards' reply counts with this instead of reloading
    private val _replyCountChanges = MutableSharedFlow<ReplyCountChange>(extraBufferCapacity = 16)
    val replyCountChanges: SharedFlow<ReplyCountChange> = _replyCountChanges.asSharedFlow()

    /** [afterId] is the id of the last reply the previous page returned (keyset paging). */
    suspend fun replies(reviewId: Int, parentReplyId: Int?, afterId: Int?): ApiResult<ReplyPage> =
        apiCall { replyApi.getReplies(reviewId, parentReplyId, afterId, PAGE_SIZE) }.map { it.toDomain() }

    suspend fun context(replyId: Int): ApiResult<ReplyContext> =
        apiCall { replyApi.getReplyContext(replyId) }.map { it.toDomain() }

    suspend fun create(reviewId: Int, parentReplyId: Int?, content: String): ApiResult<Reply> {
        val result = apiCall { replyApi.create(CreateReplyRequest(reviewId, parentReplyId, content.trim())) }
            .map { it.toDomain() }
        if (result is ApiResult.Success) _replyCountChanges.tryEmit(ReplyCountChange(reviewId, +1))
        return result
    }

    suspend fun delete(reviewId: Int, replyId: Int): ApiResult<Unit> {
        val result = apiCall { replyApi.delete(replyId) }
        if (result is ApiResult.Success) _replyCountChanges.tryEmit(ReplyCountChange(reviewId, -1))
        return result
    }

    companion object {
        const val PAGE_SIZE = 10
    }
}
