package com.reviewapp.android.ui.review

import com.reviewapp.android.domain.model.Reply
import com.reviewapp.android.domain.model.ReplyContext
import com.reviewapp.android.domain.model.ReplyPage

// The reply thread of one review as immutable state, the same rules as the web's ReplyThread /
// ReplyItem / useReplyList. Layer 2 = replies to the review, layer 3 = replies to a layer-2 reply.

/** One keyset-paged list: the replies to the review, or the children of a layer-2 reply. */
data class ReplyList(
    val items: List<Reply> = emptyList(),
    /** Id of the last reply the server returned, sent as `afterId`. Local adds don't move it. */
    val cursor: Int? = null,
    val hasMore: Boolean = false,
    val isLoading: Boolean = false,
    val error: String? = null,
    val loaded: Boolean = false,
) {
    fun loading() = copy(isLoading = true, error = null)

    fun failed(message: String) = copy(isLoading = false, error = message)

    /** Union by id (the server's copy wins), oldest first. */
    fun merged(page: ReplyPage) = copy(
        items = (page.items + items).distinctBy { it.id }.sortedBy { it.id },
        cursor = page.items.lastOrNull()?.id ?: cursor,
        hasMore = page.hasMore,
        isLoading = false,
        error = null,
        loaded = true,
    )

    fun withReply(reply: Reply) = copy(items = (items.filterNot { it.id == reply.id } + reply).sortedBy { it.id })

    fun without(id: Int) = copy(items = items.filterNot { it.id == id })

    fun mapItem(id: Int, transform: (Reply) -> Reply) = copy(items = items.map { if (it.id == id) transform(it) else it })
}

data class ReplyThreadState(
    val top: ReplyList = ReplyList(),
    val children: Map<Int, ReplyList> = emptyMap(),
    /** Layer-2 replies whose children are shown. */
    val expanded: Set<Int> = emptySet(),
    /** Non-deleted replies on both layers, like the review's `replyCount`. */
    val replyCount: Int = 0,
    /** The reply opened from a link (`?replyId=`), drawn highlighted. */
    val highlightId: Int? = null,
) {
    fun childrenOf(parentId: Int): ReplyList = children[parentId] ?: ReplyList()

    fun withTop(transform: (ReplyList) -> ReplyList) = copy(top = transform(top))

    fun withChildren(parentId: Int, transform: (ReplyList) -> ReplyList) =
        copy(children = children + (parentId to transform(childrenOf(parentId))))

    fun toggled(parentId: Int) = copy(expanded = if (parentId in expanded) expanded - parentId else expanded + parentId)

    /** A reply the user just posted: a new layer-2 reply goes to the end, a layer-3 one under its (expanded) parent. */
    fun withAdded(reply: Reply): ReplyThreadState {
        val parentId = reply.parentReplyId
            ?: return copy(top = top.withReply(reply), replyCount = replyCount + 1)
        return copy(
            top = top.mapItem(parentId) { it.copy(childCount = it.childCount + 1) },
            children = children + (parentId to childrenOf(parentId).withReply(reply)),
            expanded = expanded + parentId,
            replyCount = replyCount + 1,
        )
    }

    /**
     * A reply the user deleted. A layer-2 reply with children stays as a "deleted" placeholder so its
     * children keep their context; anything else disappears, and so does a placeholder whose last child went.
     */
    fun withDeleted(reply: Reply): ReplyThreadState {
        val count = (replyCount - 1).coerceAtLeast(0)
        val parentId = reply.parentReplyId
        if (parentId == null) {
            val current = top.items.firstOrNull { it.id == reply.id } ?: reply
            return if (current.childCount > 0) {
                copy(top = top.mapItem(reply.id) { it.asDeleted() }, replyCount = count)
            } else {
                copy(top = top.without(reply.id), children = children - reply.id, expanded = expanded - reply.id, replyCount = count)
            }
        }

        val parent = top.items.firstOrNull { it.id == parentId }?.let { it.copy(childCount = (it.childCount - 1).coerceAtLeast(0)) }
        if (parent != null && parent.isDeleted && parent.childCount == 0) {
            return copy(top = top.without(parentId), children = children - parentId, expanded = expanded - parentId, replyCount = count)
        }
        return copy(
            top = if (parent != null) top.mapItem(parentId) { parent } else top,
            children = children + (parentId to childrenOf(parentId).without(reply.id)),
            replyCount = count,
        )
    }

    companion object {
        /** Opening the thread at a reply: it (or its layer-2 parent) is shown before the first page loads. */
        fun seeded(context: ReplyContext, replyCount: Int): ReplyThreadState {
            val parent = context.parentReply
                ?: return ReplyThreadState(
                    top = ReplyList(items = listOf(context.reply)),
                    replyCount = replyCount,
                    highlightId = context.reply.id,
                )
            return ReplyThreadState(
                top = ReplyList(items = listOf(parent)),
                children = mapOf(parent.id to ReplyList(items = listOf(context.reply))),
                expanded = setOf(parent.id),
                replyCount = replyCount,
                highlightId = context.reply.id,
            )
        }
    }
}

private fun Reply.asDeleted() = copy(isDeleted = true, isOwner = false, content = "", username = null, profilePictureUrl = null)

/** The thread flattened into list rows, in display order. [parentId] null means the top-level list. */
sealed interface ThreadRow {
    val key: String

    data class Item(val reply: Reply, val highlighted: Boolean) : ThreadRow {
        override val key get() = "reply-${reply.id}"
    }
    data class Loading(val parentId: Int?) : ThreadRow {
        override val key get() = "loading-$parentId"
    }
    data class Error(val parentId: Int?, val message: String) : ThreadRow {
        override val key get() = "error-$parentId"
    }
    data class LoadMore(val parentId: Int?) : ThreadRow {
        override val key get() = "more-$parentId"
    }
    data object Empty : ThreadRow {
        override val key get() = "empty"
    }
}

fun ReplyThreadState.rows(): List<ThreadRow> = buildList {
    fun addStatus(list: ReplyList, parentId: Int?) {
        when {
            list.isLoading -> add(ThreadRow.Loading(parentId))
            list.error != null -> add(ThreadRow.Error(parentId, list.error))
            list.hasMore -> add(ThreadRow.LoadMore(parentId))
        }
    }
    top.items.forEach { reply ->
        add(ThreadRow.Item(reply, reply.id == highlightId))
        if (reply.id in expanded) {
            val children = childrenOf(reply.id)
            children.items.forEach { add(ThreadRow.Item(it, it.id == highlightId)) }
            addStatus(children, reply.id)
        }
    }
    addStatus(top, null)
    if (top.loaded && top.items.isEmpty() && !top.isLoading && top.error == null) add(ThreadRow.Empty)
}
