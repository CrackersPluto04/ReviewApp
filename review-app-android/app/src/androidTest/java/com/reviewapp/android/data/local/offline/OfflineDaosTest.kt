package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.SecureRandom

// The queries the sync and the offline reads rely on, on the real (encrypted) database
@RunWith(AndroidJUnit4::class)
class OfflineDaosTest {

    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val file = File(context.cacheDir, "offline_daos_${System.nanoTime()}.db")
    private val db = OfflineDatabase.open(context, file, ByteArray(32).also { SecureRandom().nextBytes(it) })

    @After
    fun cleanUp() {
        db.close()
        listOf("", "-wal", "-shm", "-journal").forEach { File(file.path + it).delete() }
    }

    private fun collection(id: Int, name: String = "List $id") = CollectionEntity(id, name, 1, false, "2026-11-01T00:00:00Z", "2026-11-01T00:00:00Z", 1)

    private fun media(id: String, used: Long = 0) = MediaEntity(0, id, null, "Title $id", null, null, null, used)

    @Test
    fun upsertingACollection_KeepsItsItems() = runBlocking {
        val collections = db.collections()
        collections.upsert(listOf(collection(1)))
        collections.replaceItems(1, listOf(CollectionItemEntity(1, 0, "a", 0, ""), CollectionItemEntity(1, 0, "b", 1, "")))

        // A pull upserts the collection again (renamed): the cascade must not wipe its items
        collections.upsert(listOf(collection(1, "Renamed")))

        assertEquals("Renamed", collections.byId(1)!!.collection.name)
        assertEquals(2, collections.byId(1)!!.mediaCount)
        assertEquals(listOf("a", "b"), collections.items(1).map { it.item.externalApiId })
    }

    @Test
    fun deletingACollection_DeletesItsItems() = runBlocking {
        db.collections().upsert(listOf(collection(1)))
        db.collections().replaceItems(1, listOf(CollectionItemEntity(1, 0, "a", 0, "")))

        db.collections().delete(listOf(1))

        assertTrue(db.collections().allItems().isEmpty())
    }

    @Test
    fun items_ComeInOrderWithTheirMedia() = runBlocking {
        db.media().upsert(listOf(media("a").copy(dbMediaId = 7)))
        db.collections().upsert(listOf(collection(1)))
        db.collections().replaceItems(1, listOf(CollectionItemEntity(1, 0, "b", 0, ""), CollectionItemEntity(1, 0, "a", 1, "")))

        val items = db.collections().items(1)

        assertEquals(listOf("b", "a"), items.map { it.item.externalApiId })
        assertNull(items[0].title)
        assertEquals(7, items[1].dbMediaId)
        assertEquals("Title a", items[1].title)
    }

    @Test
    fun follows_AreReplaced_ExceptTheOnesOfUsersWithAPendingChange() = runBlocking {
        val follows = db.follows()
        follows.replace(listOf(FollowEntity(1, 20), FollowEntity(1, 21), FollowEntity(22, 1)), emptySet(), listOf(
            KnownUserEntity(20, "anna", null, 0), KnownUserEntity(21, "ben", null, 0), KnownUserEntity(22, "cleo", null, 0),
        ))

        // The next pull no longer has ben, but a follow of ben waits to be sent: his row stays
        follows.replace(listOf(FollowEntity(1, 20)), keepUserIds = setOf(21), users = emptyList())

        assertEquals(listOf("anna", "ben"), follows.following(1).map { it.username })
        assertEquals(emptyList<String>(), follows.followers(1).map { it.username })
        assertEquals(2, follows.followingCount(1))
    }

    @Test
    fun followers_KnowWhetherTheUserFollowsThemBack() = runBlocking {
        db.follows().replace(listOf(FollowEntity(20, 1), FollowEntity(21, 1), FollowEntity(1, 20)), emptySet(), listOf(
            KnownUserEntity(20, "anna", null, 0), KnownUserEntity(21, "ben", null, 0),
        ))

        assertEquals(listOf("anna" to true, "ben" to false), db.follows().followers(1).map { it.username to it.isFollowed })
    }

    @Test
    fun pruning_KeepsOwnContentMediaAndTheMostRecentOthers() = runBlocking {
        db.media().upsert(listOf(media("own", used = 0), media("old", used = 1), media("recent", used = 9)))
        db.reviews().upsert(listOf(ReviewEntity(1, 0, "own", 8.0, null, null, null, 1, "", "", 1)))

        db.media().pruneUnreferenced(keep = 1)

        assertEquals(setOf("own", "recent"), db.media().all().map { it.externalApiId }.toSet())
        assertEquals(listOf("own"), db.media().ownContentMedia().map { it.externalApiId })
    }

    @Test
    fun replyCounts_SurviveAnUpsertThatCopiesThem() = runBlocking {
        db.reviews().upsert(listOf(ReviewEntity(1, 0, "a", 8.0, null, null, null, 1, "", "", 1, replyCount = 4)))

        assertEquals(listOf(ReplyCountRow(1, 4)), db.reviews().replyCounts(listOf(1, 2)))
    }

    @Test
    fun snapshots_StoreServeAndEvict() {
        val snapshots = db.snapshots()
        snapshots.put(SnapshotEntity("User/bob", "{}", savedAt = 1, lastUsedAt = 1, sizeBytes = 2, reviewIds = "3,4"))
        snapshots.touch("User/bob", 9)

        assertEquals(9L, snapshots.get("User/bob")!!.lastUsedAt)
        assertEquals(2L, snapshots.totalSize())
        assertEquals(listOf("3,4"), runBlocking { snapshots.withContent() }.map { it.reviewIds })
        snapshots.delete(listOf("User/bob"))
        assertNull(snapshots.get("User/bob"))
    }

    @Test
    fun notifications_AreReplaced_ExceptPendingOnes() = runBlocking {
        fun n(id: Int, seen: Boolean = false) = NotificationEntity(id, "System", "", "", seen, "2026-11-0${id}T00:00:00Z", null, null, null, null, null)
        db.notifications().replace(listOf(n(1), n(2), n(3, seen = true)), emptySet())

        db.notifications().replace(listOf(n(1)), keepIds = setOf(3))

        assertEquals(setOf(1, 3), db.notifications().all().map { it.id }.toSet())
        assertEquals(1, db.notifications().unseenCount())
    }
}
