package com.reviewapp.android.ui.notifications

import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationStatus
import com.reviewapp.android.domain.model.NotificationType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationListStateTest {

    private fun notification(
        id: Int,
        type: NotificationType = NotificationType.ReviewReply,
        isSeen: Boolean = false,
        actor: String? = "bob",
        reviewId: Int? = null,
        replyId: Int? = null,
    ) = AppNotification(id, type, "Title $id", "Message", isSeen, "2026-10-14T10:00:00Z", actor, null, reviewId, replyId, null)

    private fun page(vararg ids: Int, hasMore: Boolean = true, total: Int = 50) =
        NotificationPage(ids.map { notification(it) }, hasMore, total, unseenCount = 3)

    @Test
    fun merged_appendsWithoutDuplicatesAndMovesCursor() {
        val state = NotificationListState().loading().merged(page(30, 29, 28)).merged(page(28, 27, hasMore = false, total = 4))

        assertEquals(listOf(30, 29, 28, 27), state.items.map { it.id })
        assertEquals(27, state.cursor)
        assertFalse(state.hasMore)
        assertEquals(4, state.totalCount)
        assertFalse(state.isLoading)
        assertFalse(state.canLoadMore)
    }

    @Test
    fun emptyPage_keepsCursorAndIsEmpty() {
        val state = NotificationListState().merged(NotificationPage(emptyList(), false, 0, 0))
        assertNull(state.cursor)
        assertTrue(state.isEmpty)
    }

    @Test
    fun withSeen_andAllSeen_changeOnlySeenFlags() {
        val state = NotificationListState().merged(page(3, 2, 1))

        val one = state.withSeen(2, true)
        assertEquals(listOf(false, true, false), one.items.map { it.isSeen })
        assertEquals(listOf(false, false, false), one.withSeen(2, false).items.map { it.isSeen })
        assertTrue(state.allSeen().items.all { it.isSeen })
    }

    @Test
    fun without_thenRestored_putsItemBackAtItsIndex() {
        val state = NotificationListState().merged(page(3, 2, 1, total = 3))

        val removal = state.without(2)!!
        assertEquals(listOf(3, 1), removal.state.items.map { it.id })
        assertEquals(2, removal.state.totalCount)
        assertEquals(1, removal.index)

        val restored = removal.state.restored(removal.item, removal.index)
        assertEquals(listOf(3, 2, 1), restored.items.map { it.id })
        assertEquals(3, restored.totalCount)
        // Restoring twice doesn't duplicate it
        assertEquals(restored, restored.restored(removal.item, removal.index))
        assertNull(state.without(99))
    }

    @Test
    fun status_fromToggles() {
        assertEquals(NotificationStatus.All, NotificationStatus.from(seenOn = false, unseenOn = false))
        assertEquals(NotificationStatus.All, NotificationStatus.from(seenOn = true, unseenOn = true))
        assertEquals(NotificationStatus.Seen, NotificationStatus.from(seenOn = true, unseenOn = false))
        assertEquals(NotificationStatus.Unseen, NotificationStatus.from(seenOn = false, unseenOn = true))
    }

    @Test
    fun target_matchesWebRoutes() {
        assertEquals(
            NotificationTarget.Review(12, 34),
            notificationTarget(notification(1, NotificationType.ReviewReply, reviewId = 12, replyId = 34), "me"),
        )
        assertEquals(
            NotificationTarget.Review(12, 35),
            notificationTarget(notification(1, NotificationType.ReplyReply, reviewId = 12, replyId = 35), "me"),
        )
        assertEquals(
            NotificationTarget.Profile("bob"),
            notificationTarget(notification(1, NotificationType.NewFollower), "me"),
        )
        assertEquals(
            NotificationTarget.Achievements("me"),
            notificationTarget(notification(1, NotificationType.AchievementUnlocked, actor = null), "me"),
        )
        // System messages only link when they point to a review
        assertNull(notificationTarget(notification(1, NotificationType.System, actor = null), "me"))
        assertEquals(
            NotificationTarget.Review(5, null),
            notificationTarget(notification(1, NotificationType.System, actor = null, reviewId = 5), "me"),
        )
        assertNull(notificationTarget(notification(1, NotificationType.AchievementUnlocked, actor = null), null))
        assertNull(notificationTarget(notification(1, NotificationType.NewFollower, actor = null), "me"))
    }
}
