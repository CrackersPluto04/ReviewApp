package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.ReviewApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.toDto
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.ReviewDetail
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.domain.model.mediaKey
import com.reviewapp.android.util.DiscoverOptions
import com.reviewapp.android.util.MAX_SCORE
import com.reviewapp.android.util.MIN_SCORE
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

data class ReviewFilter(
    val sortBy: String = DiscoverOptions.DEFAULT_REVIEW_SORT,
    val minScore: Double = MIN_SCORE,
    val maxScore: Double = MAX_SCORE,
    val writtenOnly: Boolean = false,
) {
    val isDefault: Boolean get() = this == ReviewFilter()
}

/** Emitted after the current user created, edited ([message] set) or deleted ([deleted] with [reviewId], no message) a review. */
data class ReviewChange(
    val mediaKey: String,
    val message: String? = null,
    val deleted: Boolean = false,
    val reviewId: Int? = null,
)

class ReviewRepository(
    private val reviewApi: ReviewApi,
    private val appScope: CoroutineScope,
) {
    // Average scores of the media cards. Shared by every screen, loaded once per media and refreshed
    // when the user writes a review. Few parallel requests, so long lists don't hit the rate limit.
    private val _scoreStats = MutableStateFlow<Map<String, ScoreStats>>(emptyMap())
    val scoreStats: StateFlow<Map<String, ScoreStats>> = _scoreStats.asStateFlow()
    private val statsInFlight = mutableSetOf<String>()
    private val statsPermits = Semaphore(4)

    private val _changes = MutableSharedFlow<ReviewChange>(extraBufferCapacity = 8)
    val changes: SharedFlow<ReviewChange> = _changes.asSharedFlow()

    fun requestStats(mediaType: MediaType, externalApiId: String, force: Boolean = false) {
        val key = mediaKey(mediaType, externalApiId)
        synchronized(statsInFlight) {
            if (key in statsInFlight || (!force && key in _scoreStats.value)) return
            statsInFlight += key
        }
        appScope.launch {
            val result = statsPermits.withPermit {
                apiCall { reviewApi.getAverageScore(externalApiId, mediaType.id) }
            }
            if (result is ApiResult.Success) {
                _scoreStats.update { it + (key to result.data.toDomain()) }
            }
            synchronized(statsInFlight) { statsInFlight -= key }
        }
    }

    /** One review with its author, a 404 when it's hidden from the current user. */
    suspend fun review(id: Int): ApiResult<ReviewDetail> = apiCall { reviewApi.getReview(id) }.map { it.toDomain() }

    suspend fun recent(count: Int = 10): ApiResult<List<FeedReview>> =
        apiCall { reviewApi.getRecent(count) }.map { list -> list.map { it.toDomain() } }

    suspend fun mediaReviews(
        mediaType: MediaType,
        externalApiId: String,
        filter: ReviewFilter,
        page: Int,
    ): ApiResult<Page<MediaReview>> = apiCall {
        reviewApi.getMediaReviews(
            mediaType = mediaType.id,
            externalApiId = externalApiId,
            page = page,
            sortBy = filter.sortBy,
            minScore = filter.minScore,
            maxScore = filter.maxScore,
            hasWrittenText = filter.writtenOnly,
        )
    }.map { response -> response.toPage { it.toDomain() } }

    /** The current user's review of the media, null when they haven't reviewed it yet. */
    suspend fun ownReview(mediaType: MediaType, externalApiId: String): ApiResult<ReviewDraft?> =
        apiCall { reviewApi.checkReview(externalApiId, mediaType.id) }
            .map { response -> response.reviewData?.takeIf { response.hasReviewed }?.toDraft() }

    suspend fun save(media: Media, draft: ReviewDraft, isEdit: Boolean): ApiResult<String> {
        val request = ReviewMediaRequest(media.toDto(), draft.toDto())
        val result = apiCall {
            if (isEdit) reviewApi.editReview(request) else reviewApi.createReview(request)
        }
        return when (result) {
            is ApiResult.Error -> result
            is ApiResult.Success -> {
                val message = result.data.message ?: if (isEdit) "Review edited successfully!" else "Review created successfully!"
                requestStats(media.mediaType, media.externalApiId, force = true)
                _changes.tryEmit(ReviewChange(media.key, message))
                ApiResult.Success(message)
            }
        }
    }

    suspend fun delete(review: ProfileReview): ApiResult<Unit> {
        val result = apiCall { reviewApi.deleteReview(review.id) }
        if (result is ApiResult.Success) {
            requestStats(review.mediaType, review.externalApiId, force = true)
            _changes.tryEmit(ReviewChange(review.mediaKey, deleted = true, reviewId = review.id))
        }
        return result
    }
}
