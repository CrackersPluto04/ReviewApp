package com.reviewapp.android.data.repository

import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.FollowerApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.OfflineWriteException
import com.reviewapp.android.data.sync.OfflineWrites
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.UserSummary
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

/** Profiles, user search, a user's reviews and follow lists, and the logged-in user's follow and profile changes. */
class UserRepository(
    private val userApi: UserApi,
    private val followerApi: FollowerApi,
    private val sessionManager: SessionManager,
    private val local: LocalLibrary,
    private val offline: OfflineWrites,
) {
    // Emitted after the current user followed, unfollowed or removed someone, so the profile
    // headers (counts) and the visibility-dependent lists refresh
    private val _followChanges = MutableSharedFlow<Unit>(extraBufferCapacity = 8)

    /** Emitted after the current user followed, unfollowed or removed someone. */
    val followChanges: SharedFlow<Unit> = _followChanges.asSharedFlow()

    /**
     * A profile. Own page: the snapshot of the page when there is one (it has the counts and join date as last
     * seen), else the offline tables. Other users: snapshots only. Changes still waiting to sync (bio, picture,
     * follows) are shown as the user left them.
     */
    suspend fun profile(username: String): ApiResult<UserProfile> {
        val own = local.isMe(username)
        val result = local.networkFirst(
            own = own,
            snapshotFirst = true,
            remote = { useSnapshot -> apiCall(useSnapshot) { userApi.getProfile(username) }.map { it.toDomain() } },
            local = { profile() },
        )
        // A bio, picture or follow change waits: the header shows it as the user left it
        if (own && result is ApiResult.Success && PROFILE_TYPES.any { local.hasPending(it) }) {
            val mine = local.reads()?.let { runCatching { it.profile() }.getOrNull() } ?: return result
            return result.copy(
                data = result.data.copy(
                    bio = mine.bio,
                    profilePictureUrl = mine.profilePictureUrl,
                    followersCount = mine.followersCount,
                    followingCount = mine.followingCount,
                ),
            )
        }
        return result
    }

    /** Users whose name contains [query]. */
    suspend fun search(query: String): ApiResult<List<UserSummary>> =
        apiCall { userApi.search(query) }.map { list -> list.map { it.toDomain() } }

    /** The user's own reviews come from the offline copy when there is no connection (every one, any filter). */
    suspend fun reviews(username: String, filter: ReviewFilter, page: Int): ApiResult<Page<ProfileReview>> {
        val own = local.isMe(username)
        val result = local.networkFirst(
            own = own,
            pendingTypes = listOf(EntityType.REVIEW),
            remote = { useSnapshot ->
                apiCall(useSnapshot) {
                    userApi.getReviews(
                        username = username,
                        page = page,
                        sortBy = filter.sortBy,
                        minScore = filter.minScore,
                        maxScore = filter.maxScore,
                        hasWrittenText = filter.writtenOnly,
                    )
                }.map { response -> response.toPage { it.toDomain() } }
            },
            local = { reviews(filter, page) },
        )
        // The feed has no reply counts, the offline list shows the ones seen here
        if (own && result is ApiResult.Success && result.cachedAt == null) local.rememberReplyCounts(result.data.items)
        return result
    }

    /** The users who follow [username]. */
    suspend fun followers(username: String): ApiResult<List<UserSummary>> = local.networkFirst(
        own = local.isMe(username),
        snapshotFirst = true,
        pendingTypes = FOLLOW_TYPES,
        remote = { useSnapshot -> apiCall(useSnapshot) { userApi.getFollowers(username) }.map { list -> list.map { it.toDomain() } } },
        local = { followers() },
    )

    /** The users [username] follows. */
    suspend fun following(username: String): ApiResult<List<UserSummary>> = local.networkFirst(
        own = local.isMe(username),
        snapshotFirst = true,
        pendingTypes = FOLLOW_TYPES,
        remote = { useSnapshot -> apiCall(useSnapshot) { userApi.getFollowing(username) }.map { list -> list.map { it.toDomain() } } },
        local = { following() },
    )

    /** [username] / [profilePictureUrl]: who it is, so offline the follow lists can show them before the sync. */
    suspend fun setFollowing(userId: Int, follow: Boolean, username: String? = null, profilePictureUrl: String? = null): ApiResult<Unit> =
        notifyOnSuccess(
            if (offline.writer.canWrite()) offlineWrite { offline.writer.setFollowing(userId, follow, username, profilePictureUrl) }
            else apiCall { if (follow) followerApi.follow(userId) else followerApi.unfollow(userId) },
        )

    /** Removes someone from the user's followers. */
    suspend fun removeFollower(userId: Int): ApiResult<Unit> = notifyOnSuccess(
        if (offline.writer.canWrite()) offlineWrite { offline.writer.removeFollower(userId) }
        else apiCall { followerApi.removeFollower(userId) },
    )

    /** An empty [bio] clears it. */
    suspend fun updateBio(bio: String): ApiResult<Unit> =
        if (offline.writer.canWrite()) offlineWrite { offline.writer.updateProfile(bio = bio) }
        else apiCall { userApi.updateMe(UserUpdateRequest(bio = bio)) }.map { }

    /** Sets the profile picture URL; the session's cached user follows. */
    suspend fun updateAvatar(url: String): ApiResult<Unit> {
        val result = if (offline.writer.canWrite()) offlineWrite { offline.writer.updateProfile(profilePictureUrl = url) }
        else apiCall { userApi.updateMe(UserUpdateRequest(profilePictureUrl = url)) }.map { }
        if (result is ApiResult.Success) sessionManager.updateUser { it.copy(profilePictureUrl = url) }
        return result
    }

    private suspend fun offlineWrite(write: suspend () -> Unit): ApiResult<Unit> = try {
        write()
        ApiResult.Success(Unit)
    } catch (e: OfflineWriteException) {
        ApiResult.Error(e.message ?: "Could not save the change.")
    }

    private companion object {
        val FOLLOW_TYPES = listOf(EntityType.FOLLOW, EntityType.FOLLOWER)
        val PROFILE_TYPES = listOf(EntityType.PROFILE, EntityType.FOLLOW, EntityType.FOLLOWER)
    }

    private fun notifyOnSuccess(result: ApiResult<Unit>): ApiResult<Unit> {
        if (result is ApiResult.Success) _followChanges.tryEmit(Unit)
        return result
    }
}
