package com.reviewapp.android.data.repository

import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.NotificationApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.SetSeenRequest
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicInteger

/**
 * The current user's notifications and the unseen count behind the badge (the web's
 * `NotificationContext`). The count is refreshed on login, by the polling in `ReviewAppRoot` and
 * shortly after the user's own changes ([onMutation]); it is 0 while logged out.
 */
class NotificationRepository(
    private val api: NotificationApi,
    private val sessionManager: SessionManager,
    private val scope: CoroutineScope,
) {
    private val _unseenCount = MutableStateFlow(0)
    val unseenCount: StateFlow<Int> = _unseenCount.asStateFlow()

    // Changes on every login/logout, so responses that belong to an earlier session are ignored
    private val session = AtomicInteger()
    private var mutationRefresh: Job? = null

    private val isLoggedIn: Boolean get() = sessionManager.currentUser != null

    init {
        scope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    session.incrementAndGet()
                    _unseenCount.value = 0
                    if (userId != null) refreshUnseen()
                }
        }
    }

    suspend fun refreshUnseen() {
        if (!isLoggedIn) return
        val current = session.get()
        val result = apiCall { api.getUnseenCount() }
        if (result is ApiResult.Success && current == session.get()) _unseenCount.value = result.data.count
    }

    /**
     * Called after every successful non-GET request (the user's own actions can notify them, e.g.
     * an achievement unlock). A burst of requests causes one refresh.
     */
    fun onMutation() {
        if (!isLoggedIn) return
        synchronized(this) {
            mutationRefresh?.cancel()
            mutationRefresh = scope.launch {
                delay(MUTATION_REFRESH_DELAY_MS)
                refreshUnseen()
            }
        }
    }

    /** Optimistic badge changes of the notifications screen, never below 0. */
    fun adjustUnseen(delta: Int) = _unseenCount.update { (it + delta).coerceAtLeast(0) }

    fun setUnseen(count: Int) {
        _unseenCount.value = count.coerceAtLeast(0)
    }

    /** [afterId] is the id of the last notification of the previous page. */
    suspend fun list(status: NotificationStatus, sort: NotificationSort, afterId: Int?): ApiResult<NotificationPage> {
        val current = session.get()
        val result = apiCall { api.getNotifications(status.apiValue, sort.apiValue, afterId, PAGE_SIZE) }
            .map { it.toDomain() }
        if (result is ApiResult.Success && current == session.get()) setUnseen(result.data.unseenCount)
        return result
    }

    suspend fun setSeen(id: Int, seen: Boolean): ApiResult<Unit> = apiCall { api.setSeen(id, SetSeenRequest(seen)) }

    suspend fun markAllSeen(): ApiResult<Unit> = apiCall { api.markAllSeen() }

    /** Already gone (e.g. deleted on the web) counts as deleted. */
    suspend fun delete(id: Int): ApiResult<Unit> = when (val result = apiCall { api.delete(id) }) {
        is ApiResult.Error -> if (result.code == 404) ApiResult.Success(Unit) else result
        is ApiResult.Success -> result
    }

    suspend fun deleteSeen(): ApiResult<Unit> = apiCall { api.deleteSeen() }

    companion object {
        const val PAGE_SIZE = 20
        const val MUTATION_REFRESH_DELAY_MS = 800L
    }
}
