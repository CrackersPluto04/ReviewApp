package com.reviewapp.android.ui.notifications

import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationType

/**
 * The loaded notifications of the current filter, with the same local updates as the web's
 * `NotificationsDialog` (actions change the list at once and are put back if the request fails).
 */
data class NotificationListState(
    val items: List<AppNotification> = emptyList(),
    /** The id of the last notification the server returned, sent as `afterId`. */
    val cursor: Int? = null,
    val hasMore: Boolean = false,
    /** Notifications matching the current filter, for the title. */
    val totalCount: Int = 0,
    val isLoading: Boolean = false,
    val error: String? = null,
    val loaded: Boolean = false,
) {
    val canLoadMore: Boolean get() = loaded && hasMore && !isLoading && error == null
    val isEmpty: Boolean get() = loaded && items.isEmpty() && !isLoading && error == null

    fun loading() = copy(isLoading = true, error = null)

    fun failed(message: String) = copy(isLoading = false, error = message)

    /** Appends a page, skipping ids that are already shown. */
    fun merged(page: NotificationPage): NotificationListState {
        val seen = items.mapTo(HashSet()) { it.id }
        return copy(
            items = items + page.items.filter { seen.add(it.id) },
            cursor = page.items.lastOrNull()?.id ?: cursor,
            hasMore = page.hasMore,
            totalCount = page.totalCount,
            isLoading = false,
            error = null,
            loaded = true,
        )
    }

    fun withSeen(id: Int, seen: Boolean) = copy(items = items.map { if (it.id == id) it.copy(isSeen = seen) else it })

    fun allSeen() = copy(items = items.map { if (it.isSeen) it else it.copy(isSeen = true) })

    /** Removes a notification; returns null when it isn't in the list. */
    fun without(id: Int): Removal? {
        val index = items.indexOfFirst { it.id == id }
        if (index < 0) return null
        val state = copy(items = items.filterIndexed { i, _ -> i != index }, totalCount = (totalCount - 1).coerceAtLeast(0))
        return Removal(state, items[index], index)
    }

    /** Puts back a notification whose deletion failed, at its old place. */
    fun restored(item: AppNotification, index: Int): NotificationListState {
        if (items.any { it.id == item.id }) return this
        val list = items.toMutableList().apply { add(index.coerceIn(0, size), item) }
        return copy(items = list, totalCount = totalCount + 1)
    }

    data class Removal(val state: NotificationListState, val item: AppNotification, val index: Int)
}

/** Where "Take me there" goes. */
sealed interface NotificationTarget {
    data class Review(val reviewId: Int, val replyId: Int?) : NotificationTarget
    data class Profile(val username: String) : NotificationTarget
    /** The current user's Achievements tab. */
    data class Achievements(val username: String) : NotificationTarget
}

/**
 * The web's `getNotificationRoute`: replies (and system messages about a review) open the review,
 * a new follower opens their profile, an unlock opens the user's own achievements.
 */
fun notificationTarget(notification: AppNotification, currentUsername: String?): NotificationTarget? =
    when (notification.type) {
        NotificationType.AchievementUnlocked -> currentUsername?.let { NotificationTarget.Achievements(it) }
        NotificationType.NewFollower -> notification.actorUsername?.let { NotificationTarget.Profile(it) }
        NotificationType.ReviewReply, NotificationType.ReplyReply, NotificationType.System ->
            notification.reviewId?.let { NotificationTarget.Review(it, notification.replyId) }
    }
