package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.LocalCollection
import com.reviewapp.android.data.local.offline.LocalReview
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.NotificationEntity
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import com.reviewapp.android.domain.model.Visibility
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// The offline copy answers like the API (ReviewService.GetUserReviewsAsync, CollectionService, NotificationService)
class LocalQueriesTest {

    private fun review(id: Int, score: Double, created: String, updated: String = created, text: String? = null, pros: String? = null) =
        LocalReview(
            ReviewEntity(id, 0, "m$id", score, text, pros, null, Visibility.Private.id, created, updated, version = 1, replyCount = id),
            title = "Title $id",
            posterUrl = null,
        )

    private val reviews = listOf(
        review(1, 9.0, "2026-10-01T10:00:00Z", "2026-11-03T10:00:00Z", text = "Loved it"),
        review(2, 4.5, "2026-10-03T10:00:00.5Z"),
        review(3, 7.0, "2026-10-02T10:00:00.1234567Z", pros = "Music"),
    )

    @Test
    fun reviews_NewestFirstByDefault_WithVaryingFractions() {
        assertEquals(listOf(2, 3, 1), LocalReviewQuery.page(reviews, ReviewFilter(), 1).items.map { it.id })
    }

    @Test
    fun reviews_EverySortOfTheApi() {
        fun ids(sort: String) = LocalReviewQuery.page(reviews, ReviewFilter(sortBy = sort), 1).items.map { it.id }
        assertEquals(listOf(1, 3, 2), ids("created_asc"))
        assertEquals(listOf(1, 2, 3), ids("updated_desc"))
        assertEquals(listOf(3, 2, 1), ids("updated_asc"))
        assertEquals(listOf(1, 3, 2), ids("score_desc"))
        assertEquals(listOf(2, 3, 1), ids("score_asc"))
    }

    @Test
    fun reviews_FilterByScoreAndWrittenText() {
        assertEquals(listOf(3, 1), LocalReviewQuery.page(reviews, ReviewFilter(minScore = 5.0, maxScore = 10.0), 1).items.map { it.id })
        assertEquals(listOf(3, 1), LocalReviewQuery.page(reviews, ReviewFilter(writtenOnly = true), 1).items.map { it.id })
    }

    @Test
    fun reviews_PageLikeTheApi_AndLookLikeItsItems() {
        val page = LocalReviewQuery.page(reviews, ReviewFilter(), page = 2, pageSize = 2)

        assertEquals(listOf(1), page.items.map { it.id })
        assertEquals(2, page.totalPages)
        assertEquals(3, page.totalCount)
        assertFalse(page.hasMore)
        val item = page.items.single()
        assertEquals("2026-10-01", item.createdAt)
        assertEquals(Visibility.Private, item.visibility)
        assertTrue(item.isOwner)
        assertEquals(1, item.replyCount)
        assertEquals("Title 1", item.mediaTitle)
    }

    @Test
    fun collections_SortLikeTheApi() {
        fun c(id: Int, name: String, created: String) = LocalCollection(CollectionEntity(id, name, 1, id == 1, created, created, 1), mediaCount = id)
        val all = listOf(c(1, "Favourites", "2026-01-01T00:00:00Z"), c(2, "watch later", "2026-03-01T00:00:00Z"), c(3, "Best", "2026-02-01T00:00:00Z"))

        assertEquals(listOf(1, 3, 2), LocalCollectionQuery.sorted(all, "createdAt_asc").map { it.id })
        assertEquals(listOf(2, 3, 1), LocalCollectionQuery.sorted(all, "createdAt_desc").map { it.id })
        assertEquals(listOf(3, 1, 2), LocalCollectionQuery.sorted(all, "name_asc").map { it.id })
        assertEquals(listOf(2, 1, 3), LocalCollectionQuery.sorted(all, "name_desc").map { it.id })
        val favourites = LocalCollectionQuery.sorted(all, "createdAt_asc").first()
        assertTrue(favourites.isDefault && favourites.isOwner)
        assertEquals("2026-01-01", favourites.createdAt)
        assertEquals(1, favourites.mediaCount)
    }

    private fun notification(id: Int, seen: Boolean, created: String, deleted: Boolean = false) =
        NotificationEntity(id, "System", "T$id", "M$id", seen, created, null, null, null, null, null, deleted)

    private val notifications = listOf(
        notification(1, seen = true, created = "2026-11-01T10:00:00Z"),
        notification(2, seen = false, created = "2026-11-02T10:00:00Z"),
        notification(3, seen = false, created = "2026-11-03T10:00:00Z"),
        notification(4, seen = false, created = "2026-11-04T10:00:00Z", deleted = true),
    )

    @Test
    fun notifications_KeysetPagesNewestFirst_WithoutDeletedOnes() {
        val first = LocalNotificationQuery.page(notifications, NotificationStatus.All, NotificationSort.Newest, afterId = null, pageSize = 2)
        assertEquals(listOf(3, 2), first.items.map { it.id })
        assertTrue(first.hasMore)
        assertEquals(3, first.totalCount)
        assertEquals(2, first.unseenCount)

        val second = LocalNotificationQuery.page(notifications, NotificationStatus.All, NotificationSort.Newest, afterId = 2, pageSize = 2)
        assertEquals(listOf(1), second.items.map { it.id })
        assertFalse(second.hasMore)
    }

    @Test
    fun notifications_FilterAndOldestFirst() {
        assertEquals(listOf(1), LocalNotificationQuery.page(notifications, NotificationStatus.Seen, NotificationSort.Newest, null, 20).items.map { it.id })
        assertEquals(listOf(2, 3), LocalNotificationQuery.page(notifications, NotificationStatus.Unseen, NotificationSort.Oldest, null, 20).items.map { it.id })
        // A cursor that isn't there any more ends the list instead of starting over
        assertEquals(emptyList<Int>(), LocalNotificationQuery.page(notifications, NotificationStatus.All, NotificationSort.Newest, 99, 20).items.map { it.id })
    }

    private fun media(id: String, title: String, type: MediaType = MediaType.Movie, creator: String? = null, used: Long = 0) =
        MediaEntity(type.id, id, null, title, null, null, creator, used)

    @Test
    fun search_MatchesEveryWord_IgnoringCaseAndAccents() {
        val library = listOf(media("1", "Amélie"), media("2", "The Dark Knight"), media("3", "Knight Rider", MediaType.Series))

        assertEquals(listOf("1"), OfflineSearch.search(library, "amelie").map { it.externalApiId })
        assertEquals(listOf("2"), OfflineSearch.search(library, "dark KNIGHT").map { it.externalApiId })
        assertEquals(emptyList<String>(), OfflineSearch.search(library, "  ").map { it.externalApiId })
    }

    @Test
    fun search_RanksTitleStartsFirst_ThenRecentlyUsed_AndFiltersByType() {
        val library = listOf(
            media("old", "The Knight", used = 1),
            media("recent", "Dark Knight", used = 9),
            media("start", "Knight Rider", used = 0),
            media("song", "Kraftwerk", MediaType.Music, creator = "Knight Band"),
        )

        assertEquals(listOf("start", "recent", "old", "song"), OfflineSearch.search(library, "knight").map { it.externalApiId })
        assertEquals(listOf("song"), OfflineSearch.search(library, "knight", MediaType.Music).map { it.externalApiId })
    }
}
