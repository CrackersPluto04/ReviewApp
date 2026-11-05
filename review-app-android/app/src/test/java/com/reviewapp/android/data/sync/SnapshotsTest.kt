package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.SnapshotUsage
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SnapshotsTest {

    @Test
    fun policy_KeepsReadsOfContent_WithTheirQuery() {
        assertEquals("Media/discover/movie?page=2&sortBy=popularity.desc", SnapshotPolicy.keyOf("Media/discover/movie", "page=2&sortBy=popularity.desc"))
        assertEquals("Review/42", SnapshotPolicy.keyOf("Review/42", null))
        assertEquals("User/bob", SnapshotPolicy.keyOf("User/bob", ""))
    }

    @Test
    fun policy_NeverKeepsSessionsPaymentsSearchesOrTheOfflineTablesContent() {
        listOf(
            "Auth/check-auth", "Security/status", "Subscription/status", "Recognition/allowance", "Sync/changes",
            "Media/search/all", "Media/search/movie", "User/search",
            "Notification", "Notification/unseen-count", "Collection/me/memberships", "Review/check",
        ).forEach { assertNull(it, SnapshotPolicy.keyOf(it, null)) }
    }

    @Test
    fun index_FindsTheReviewsAndCollectionsASnapshotShows() {
        assertEquals(setOf(42), SnapshotIndex.contentOf("Review/42", "{}").reviewIds)
        assertEquals(setOf(9), SnapshotIndex.contentOf("Collection/9", "{}").collectionIds)
        assertEquals(
            setOf(1, 2),
            SnapshotIndex.contentOf("Review/media/0/550?page=1", """{"items":[{"id":1},{"id":2}],"totalCount":2}""").reviewIds,
        )
        assertEquals(setOf(3), SnapshotIndex.contentOf("User/bob/reviews?page=1", """{"items":[{"id":3,"score":7}]}""").reviewIds)
        assertEquals(setOf(4, 5), SnapshotIndex.contentOf("Review/recent?count=10", """[{"id":4},{"id":5}]""").reviewIds)
        assertEquals(setOf(6), SnapshotIndex.contentOf("User/bob/collections?sortBy=name_asc", """[{"id":6,"name":"x"}]""").collectionIds)
        assertEquals(setOf(42), SnapshotIndex.contentOf("ReviewReply?reviewId=42&pageSize=10", """{"items":[]}""").reviewIds)
        assertEquals(setOf(43), SnapshotIndex.contentOf("ReviewReply/7", """{"reviewID":43,"reply":{"id":7}}""").reviewIds)
    }

    @Test
    fun index_IgnoresEverythingElse_AndBrokenBodies() {
        assertEquals(SnapshotContent(), SnapshotIndex.contentOf("Media/media/0/550", """{"title":"Fight Club"}"""))
        assertEquals(SnapshotContent(), SnapshotIndex.contentOf("User/bob/reviews?page=1", "not json"))
    }

    @Test
    fun content_RoundTripsThroughItsColumns() {
        val content = SnapshotContent(reviewIds = setOf(3, 1), collectionIds = emptySet())
        assertEquals("1,3", content.reviewsCsv())
        assertEquals(setOf(1, 3), SnapshotContent.parseIds(content.reviewsCsv()))
        assertEquals(emptySet<Int>(), SnapshotContent.parseIds(""))
    }

    @Test
    fun eviction_DropsTheLeastRecentlyUsed_UntilWithinTheBudget() {
        val usage = listOf(
            SnapshotUsage("new", 400, lastUsedAt = 30),
            SnapshotUsage("old", 300, lastUsedAt = 10),
            SnapshotUsage("middle", 300, lastUsedAt = 20),
        )

        assertEquals(emptyList<String>(), SnapshotEviction.keysToEvict(usage, budgetBytes = 1000))
        assertEquals(listOf("old"), SnapshotEviction.keysToEvict(usage, budgetBytes = 700))
        assertEquals(listOf("old", "middle"), SnapshotEviction.keysToEvict(usage, budgetBytes = 500))
        assertEquals(listOf("old", "middle", "new"), SnapshotEviction.keysToEvict(usage, budgetBytes = 0))
    }
}
