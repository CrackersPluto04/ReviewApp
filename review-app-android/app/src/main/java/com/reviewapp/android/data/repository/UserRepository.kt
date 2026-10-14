package com.reviewapp.android.data.repository

import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.FollowerApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.UserSummary
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

class UserRepository(
    private val userApi: UserApi,
    private val followerApi: FollowerApi,
    private val sessionManager: SessionManager,
) {
    // Emitted after the current user followed, unfollowed or removed someone, so the profile
    // headers (counts) and the visibility-dependent lists refresh
    private val _followChanges = MutableSharedFlow<Unit>(extraBufferCapacity = 8)
    val followChanges: SharedFlow<Unit> = _followChanges.asSharedFlow()

    suspend fun profile(username: String): ApiResult<UserProfile> =
        apiCall { userApi.getProfile(username) }.map { it.toDomain() }

    suspend fun search(query: String): ApiResult<List<UserSummary>> =
        apiCall { userApi.search(query) }.map { list -> list.map { it.toDomain() } }

    suspend fun reviews(username: String, filter: ReviewFilter, page: Int): ApiResult<Page<ProfileReview>> = apiCall {
        userApi.getReviews(
            username = username,
            page = page,
            sortBy = filter.sortBy,
            minScore = filter.minScore,
            maxScore = filter.maxScore,
            hasWrittenText = filter.writtenOnly,
        )
    }.map { response -> response.toPage { it.toDomain() } }

    suspend fun followers(username: String): ApiResult<List<UserSummary>> =
        apiCall { userApi.getFollowers(username) }.map { list -> list.map { it.toDomain() } }

    suspend fun following(username: String): ApiResult<List<UserSummary>> =
        apiCall { userApi.getFollowing(username) }.map { list -> list.map { it.toDomain() } }

    suspend fun setFollowing(userId: Int, follow: Boolean): ApiResult<Unit> = notifyOnSuccess(
        apiCall { if (follow) followerApi.follow(userId) else followerApi.unfollow(userId) },
    )

    suspend fun removeFollower(userId: Int): ApiResult<Unit> =
        notifyOnSuccess(apiCall { followerApi.removeFollower(userId) })

    /** An empty [bio] clears it. */
    suspend fun updateBio(bio: String): ApiResult<Unit> =
        apiCall { userApi.updateMe(UserUpdateRequest(bio = bio)) }.map { }

    suspend fun updateAvatar(url: String): ApiResult<Unit> {
        val result = apiCall { userApi.updateMe(UserUpdateRequest(profilePictureUrl = url)) }.map { }
        if (result is ApiResult.Success) sessionManager.updateUser { it.copy(profilePictureUrl = url) }
        return result
    }

    private fun notifyOnSuccess(result: ApiResult<Unit>): ApiResult<Unit> {
        if (result is ApiResult.Success) _followChanges.tryEmit(Unit)
        return result
    }
}
