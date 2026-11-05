package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.ReviewApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.toDto
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.OfflineWriteException
import com.reviewapp.android.data.sync.OfflineWrites
import com.reviewapp.android.data.sync.OpTypes
import com.reviewapp.android.data.sync.OpWait
import com.reviewapp.android.data.sync.OutboxDescriber
import com.reviewapp.android.data.local.offline.EntityType
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
    /** Not a user's change: a sync updated the offline copy (doesn't trigger another sync). */
    val fromSync: Boolean = false,
)

class ReviewRepository(
    private val reviewApi: ReviewApi,
    private val appScope: CoroutineScope,
    private val local: LocalLibrary,
    private val offline: OfflineWrites,
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

    /** The average score of a media, loaded now (the library download keeps it for offline). */
    suspend fun statsForOffline(mediaType: MediaType, externalApiId: String): ApiResult<Unit> {
        val result = apiCall { reviewApi.getAverageScore(externalApiId, mediaType.id) }
        if (result is ApiResult.Success) _scoreStats.update { it + (mediaKey(mediaType, externalApiId) to result.data.toDomain()) }
        return result.map { }
    }

    /** One review with its author, a 404 when it's hidden from the current user. */
    suspend fun review(id: Int): ApiResult<ReviewDetail> {
        // Created offline: the real id once the backend has it
        val realId = local.resolve(EntityType.REVIEW, id)
        return local.networkFirst(
            own = local.ownsReview(realId),
            pendingTypes = listOf(EntityType.REVIEW),
            remote = { useSnapshot ->
                if (realId < 0) ApiResult.Error("Not found.", 404)
                else apiCall(useSnapshot) { reviewApi.getReview(realId) }.map { it.toDomain() }
            },
            local = { reviewDetail(realId) },
        )
    }

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
    suspend fun ownReview(mediaType: MediaType, externalApiId: String): ApiResult<ReviewDraft?> {
        // An offline change of the user's reviews waits: the copy has the review as the user left it
        local.reads()?.takeIf { it.hasPending(EntityType.REVIEW) }?.let { reads ->
            try {
                return ApiResult.Success(reads.ownReview(mediaType, externalApiId))
            } catch (_: Exception) {
                // Closed meanwhile: ask the API
            }
        }
        val result = apiCall(useSnapshot = false) { reviewApi.checkReview(externalApiId, mediaType.id) }
            .map { response -> response.reviewData?.takeIf { response.hasReviewed }?.toDraft() }
        if (result !is ApiResult.Error || !result.isNetwork) return result
        // "Not reviewed" is an answer too, so this can't go through networkFirst (null = no local answer there)
        val reads = local.reads() ?: return result
        return try {
            ApiResult.Success(reads.ownReview(mediaType, externalApiId), cachedAt = reads.lastSyncedAt())
        } catch (_: Exception) {
            result
        }
    }

    suspend fun save(media: Media, draft: ReviewDraft, isEdit: Boolean): ApiResult<String> {
        if (offline.writer.canWrite()) return saveOffline(media, draft, isEdit)
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

    /**
     * Offline mode: saved in the offline copy and queued. Online the answer is awaited a moment, so a review that
     * already exists (written on the web meanwhile) is refused like before; an edit in conflict stays for Sync issues.
     */
    private suspend fun saveOffline(media: Media, draft: ReviewDraft, isEdit: Boolean): ApiResult<String> {
        val op = try {
            offline.writer.saveReview(media, draft)
        } catch (e: OfflineWriteException) {
            return ApiResult.Error(e.message ?: "Could not save the review.")
        }
        val wait = offline.engine.awaitOp(op.opId)
        if (wait is OpWait.Issue && op.type == OpTypes.REVIEW_CREATE) {
            offline.writer.discard(op.seq)
            return ApiResult.Error(OutboxDescriber.reasonMessage(wait.reason))
        }
        val message = when (wait) {
            OpWait.Applied -> if (isEdit) "Review edited successfully!" else "Review created successfully!"
            is OpWait.Issue -> "Review saved. It was changed elsewhere too, see Sync issues."
            OpWait.Queued -> "Review saved. It will sync when you're back online."
        }
        _changes.tryEmit(ReviewChange(media.key, message))
        return ApiResult.Success(message)
    }

    /** A sync applied changes: the owner's lists reload (a review created offline now has its real id). */
    fun onSynced() {
        _changes.tryEmit(ReviewChange(mediaKey = "", fromSync = true))
    }

    suspend fun delete(review: ProfileReview): ApiResult<Unit> {
        if (offline.writer.canWrite()) {
            return try {
                offline.writer.deleteReview(review.id)
                _changes.tryEmit(ReviewChange(review.mediaKey, deleted = true, reviewId = review.id))
                ApiResult.Success(Unit)
            } catch (e: OfflineWriteException) {
                ApiResult.Error(e.message ?: "Could not delete the review.")
            }
        }
        val result = apiCall { reviewApi.deleteReview(review.id) }
        if (result is ApiResult.Success) {
            requestStats(review.mediaType, review.externalApiId, force = true)
            _changes.tryEmit(ReviewChange(review.mediaKey, deleted = true, reviewId = review.id))
        }
        return result
    }
}
