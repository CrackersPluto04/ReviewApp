package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.FollowEntity
import com.reviewapp.android.data.remote.dto.NotificationDto
import com.reviewapp.android.data.remote.dto.SyncAccountDto
import com.reviewapp.android.data.remote.dto.SyncChangesDto
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncCollectionItemDto
import com.reviewapp.android.data.remote.dto.SyncMediaDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.remote.dto.UserCompactDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ChangesApplierTest {

    private val dune = SyncMediaDto(dbMediaId = 11, mediaType = 0, externalApiId = "438631", title = "Dune", posterUrl = "")
    private val album = SyncMediaDto(dbMediaId = 12, mediaType = 2, externalApiId = "abc", title = "Album")

    private fun review(id: Int, media: SyncMediaDto = dune, version: Long = 100) =
        SyncReviewDto(id, media, score = 8.0, reviewText = "Great", visibility = 0, createdAt = "2026-11-01T10:00:00Z", updatedAt = "2026-11-02T10:00:00Z", version = version)

    private fun collection(id: Int, vararg media: SyncMediaDto) =
        SyncCollectionDto(id, "List $id", visibility = 1, items = media.mapIndexed { i, m -> SyncCollectionItemDto(m, i, "2026-11-01T10:00:00Z") }, version = 200)

    @Test
    fun aPage_BecomesRows_WithEveryMediaOnce() {
        val page = SyncChangesDto(
            reviews = listOf(review(1), review(2, album)),
            deletedReviewIds = listOf(3),
            collections = listOf(collection(5, dune, album)),
            deletedCollectionIds = listOf(6),
        )

        val set = ChangesApplier.plan(page, PendingEntities(), nowMillis = 42)

        assertEquals(listOf(1, 2), set.reviews.map { it.id })
        assertEquals(0, set.reviews[0].visibility)
        assertEquals(100L, set.reviews[0].version)
        assertEquals(listOf(3), set.deletedReviewIds)
        assertEquals(listOf(5), set.collections.map { it.id })
        assertEquals(listOf("438631" to 0, "abc" to 1), set.collectionItems.getValue(5).map { it.externalApiId to it.orderIndex })
        assertEquals(listOf(6), set.deletedCollectionIds)
        assertEquals(listOf("438631", "abc"), set.media.map { it.externalApiId })
        // A blank poster is no poster
        assertNull(set.media[0].posterUrl)
        assertEquals(11, set.media[0].dbMediaId)
        assertTrue(set.shadows.isEmpty())
    }

    @Test
    fun pendingEntities_AreNeverOverwritten_OnlyTheirShadowMoves() {
        val page = SyncChangesDto(
            reviews = listOf(review(1, version = 300), review(2)),
            deletedReviewIds = listOf(7),
            collections = listOf(collection(5, dune)),
            deletedCollectionIds = listOf(8),
        )
        val pending = PendingEntities(reviewIds = setOf(1, 7), collectionIds = setOf(5, 8))

        val set = ChangesApplier.plan(page, pending, 0)

        assertEquals(listOf(2), set.reviews.map { it.id })
        assertTrue(set.collections.isEmpty())
        assertTrue(set.collectionItems.isEmpty())
        // Deleted on the backend while edited here: kept, the conflict comes with the push
        assertTrue(set.deletedReviewIds.isEmpty())
        assertTrue(set.deletedCollectionIds.isEmpty())
        val shadows = set.shadows.associateBy { it.entityType to it.entityId }
        assertEquals(300L, shadows.getValue(EntityType.REVIEW to 1).version)
        assertTrue(shadows.getValue(EntityType.REVIEW to 1).json.contains("\"version\":300"))
        assertEquals(ServerShadow.DELETED, shadows.getValue(EntityType.REVIEW to 7).json)
        assertEquals(200L, shadows.getValue(EntityType.COLLECTION to 5).version)
        assertEquals(ServerShadow.DELETED, shadows.getValue(EntityType.COLLECTION to 8).json)
    }

    @Test
    fun staleIds_AfterAFullResync_SpareOfflineCreationsAndPendingOnes() {
        assertEquals(listOf(4), ChangesApplier.staleIds(listOf(1, 2, 4, -3, 9), seenIds = setOf(1, 2), pendingIds = setOf(9)))
    }

    @Test
    fun account_ReplacesFollowsAndNotifications_ExceptPendingOnes() {
        val account = SyncAccountDto(
            userId = 7,
            username = "me",
            bio = "Hi",
            profilePictureUrl = "",
            following = listOf(UserCompactDto(20, "anna"), UserCompactDto(21, "ben")),
            followers = listOf(UserCompactDto(20, "anna"), UserCompactDto(22, "cleo")),
            notifications = listOf(NotificationDto(1, "System", isSeen = false), NotificationDto(2, "NewFollower", isSeen = true)),
            unseenNotificationCount = 1,
        )

        val change = ChangesApplier.account(account, PendingEntities(followUserIds = setOf(21), notificationIds = setOf(2)), 5)

        assertEquals("Hi", change.profile!!.bio)
        assertNull(change.profile!!.profilePictureUrl)
        assertEquals(setOf(FollowEntity(7, 20), FollowEntity(20, 7), FollowEntity(22, 7)), change.follows.toSet())
        assertEquals(setOf(21), change.keepFollowsOf)
        assertEquals(listOf(20, 21, 22), change.knownUsers.map { it.userId })
        assertEquals(listOf(1), change.notifications!!.map { it.id })
        assertEquals(setOf(2), change.keepNotificationIds)
        assertEquals(1, change.unseenCount)
    }

    @Test
    fun account_KeepsLocalProfileAndNotifications_WhileABulkOrProfileChangeWaits() {
        val account = SyncAccountDto(userId = 7, notifications = listOf(NotificationDto(1)))

        val change = ChangesApplier.account(account, PendingEntities(profile = true, notificationBulk = true), 0)

        assertNull(change.profile)
        assertNull(change.notifications)
    }
}
