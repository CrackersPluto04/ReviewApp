package com.reviewapp.android.data.repository

import android.util.Log
import com.reviewapp.android.data.remote.AchievementApi
import com.reviewapp.android.data.remote.ApiJson
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.UnlockedAchievementDto
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.UnlockedAchievement
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

class AchievementRepository(private val api: AchievementApi) {

    // Tiers unlocked by the current user's requests, shown as banners and reloading the owner's tab
    private val _unlocks = MutableSharedFlow<UnlockedAchievement>(extraBufferCapacity = 16)
    val unlocks: SharedFlow<UnlockedAchievement> = _unlocks.asSharedFlow()

    suspend fun achievements(username: String): ApiResult<List<Achievement>> =
        apiCall { api.getAchievements(username) }.map { list -> list.map { it.toDomain() } }

    /** Called by the OkHttp interceptor for every response with the [UNLOCK_HEADER]. */
    fun onUnlockHeader(raw: String) {
        val unlocked = parseUnlockHeader(raw)
        if (unlocked == null) {
            // A bad header must never break the request it came with
            Log.w(TAG, "Could not parse $UNLOCK_HEADER")
            return
        }
        unlocked.forEach { _unlocks.tryEmit(it) }
    }

    companion object {
        const val UNLOCK_HEADER = "X-Unlocked-Achievements"
        private const val TAG = "AchievementRepository"

        /** The header is a raw JSON array of `{ groupCode, title, tier }`; null when it can't be read. */
        fun parseUnlockHeader(raw: String): List<UnlockedAchievement>? = try {
            ApiJson.decodeFromString<List<UnlockedAchievementDto>>(raw).map { it.toDomain() }
        } catch (_: Exception) {
            null
        }
    }
}
