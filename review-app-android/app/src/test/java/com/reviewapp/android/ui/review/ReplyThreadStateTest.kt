package com.reviewapp.android.ui.review

import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReplyContext
import com.reviewapp.android.domain.model.ReplyPage
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ReplyThreadStateTest {

    private fun reply(id: Int, parent: Int? = null, children: Int = 0, deleted: Boolean = false) = Reply(
        id = id,
        parentReplyId = parent,
        content = if (deleted) "" else "reply $id",
        username = if (deleted) null else "user$id",
        profilePictureUrl = null,
        createdAt = "2026-10-14",
        isDeleted = deleted,
        isOwner = !deleted,
        childCount = children,
    )

    @Test
    fun merged_dedupesSortsAndMovesCursorToServerItems() {
        val list = ReplyList(items = listOf(reply(5), reply(12)))
            .merged(ReplyPage(listOf(reply(3), reply(5), reply(7)), hasMore = true))

        assertEquals(listOf(3, 5, 7, 12), list.items.map { it.id })
        // 12 was added locally, the next page must continue after the server's 7
        assertEquals(7, list.cursor)
        assertTrue(list.hasMore)
        assertTrue(list.loaded)
    }

    @Test
    fun merged_emptyPageKeepsCursor() {
        val list = ReplyList(cursor = 9).merged(ReplyPage(emptyList(), hasMore = false))
        assertEquals(9, list.cursor)
        assertFalse(list.hasMore)
    }

    @Test
    fun added_topLevelGoesLastAndCounts() {
        val state = ReplyThreadState(top = ReplyList(items = listOf(reply(1), reply(2))), replyCount = 2)
            .withAdded(reply(10))

        assertEquals(listOf(1, 2, 10), state.top.items.map { it.id })
        assertEquals(3, state.replyCount)
    }

    @Test
    fun added_layerThreeExpandsParentAndCountsChild() {
        val state = ReplyThreadState(top = ReplyList(items = listOf(reply(1, children = 1))), replyCount = 2)
            .withAdded(reply(20, parent = 1))

        assertTrue(1 in state.expanded)
        assertEquals(2, state.top.items.single().childCount)
        assertEquals(listOf(20), state.childrenOf(1).items.map { it.id })
        assertEquals(3, state.replyCount)
    }

    @Test
    fun deleted_topLevelWithChildrenBecomesPlaceholder() {
        val state = ReplyThreadState(top = ReplyList(items = listOf(reply(1, children = 2))), replyCount = 3)
            .withDeleted(reply(1, children = 2))

        val placeholder = state.top.items.single()
        assertTrue(placeholder.isDeleted)
        assertFalse(placeholder.isOwner)
        assertNull(placeholder.username)
        assertEquals(2, state.replyCount)
    }

    @Test
    fun deleted_topLevelWithoutChildrenIsRemoved() {
        val state = ReplyThreadState(top = ReplyList(items = listOf(reply(1), reply(2))), replyCount = 2)
            .withDeleted(reply(1))

        assertEquals(listOf(2), state.top.items.map { it.id })
        assertEquals(1, state.replyCount)
    }

    @Test
    fun deleted_layerThreeUpdatesParentCount() {
        val state = ReplyThreadState(
            top = ReplyList(items = listOf(reply(1, children = 2))),
            children = mapOf(1 to ReplyList(items = listOf(reply(4, parent = 1), reply(5, parent = 1)))),
            expanded = setOf(1),
            replyCount = 3,
        ).withDeleted(reply(4, parent = 1))

        assertEquals(1, state.top.items.single().childCount)
        assertEquals(listOf(5), state.childrenOf(1).items.map { it.id })
        assertEquals(2, state.replyCount)
    }

    @Test
    fun deleted_lastChildOfPlaceholderRemovesBoth() {
        val state = ReplyThreadState(
            top = ReplyList(items = listOf(reply(1, children = 1, deleted = true), reply(2))),
            children = mapOf(1 to ReplyList(items = listOf(reply(4, parent = 1)))),
            expanded = setOf(1),
            replyCount = 2,
        ).withDeleted(reply(4, parent = 1))

        assertEquals(listOf(2), state.top.items.map { it.id })
        assertFalse(1 in state.expanded)
        assertFalse(1 in state.children)
        assertEquals(1, state.replyCount)
    }

    @Test
    fun seeded_layerTwoHighlightsIt() {
        val state = ReplyThreadState.seeded(ReplyContext(7, reply(3), parentReply = null), replyCount = 5)

        assertEquals(listOf(3), state.top.items.map { it.id })
        assertEquals(3, state.highlightId)
        assertTrue(state.expanded.isEmpty())
        assertFalse(state.top.loaded)
    }

    @Test
    fun seeded_layerThreeOpensUnderItsParent() {
        val state = ReplyThreadState.seeded(ReplyContext(7, reply(9, parent = 3), reply(3, children = 1)), replyCount = 5)

        assertEquals(listOf(3), state.top.items.map { it.id })
        assertEquals(setOf(3), state.expanded)
        assertEquals(listOf(9), state.childrenOf(3).items.map { it.id })
        assertEquals(9, state.highlightId)

        val rows = state.rows()
        assertEquals(listOf("reply-3", "reply-9"), rows.map { it.key })
        assertTrue((rows[1] as ThreadRow.Item).highlighted)
    }

    @Test
    fun rows_showChildrenStatusAndEmptyState() {
        val state = ReplyThreadState(
            top = ReplyList(items = listOf(reply(1, children = 3)), hasMore = true, loaded = true),
            children = mapOf(1 to ReplyList(items = listOf(reply(4, parent = 1)), isLoading = true)),
            expanded = setOf(1),
        )
        assertEquals(listOf("reply-1", "reply-4", "loading-1", "more-null"), state.rows().map { it.key })

        val empty = ReplyThreadState(top = ReplyList(loaded = true))
        assertEquals(listOf(ThreadRow.Empty), empty.rows())
    }
}
