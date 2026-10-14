package com.reviewapp.android.data.repository

import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.util.moved
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class MyCollectionsTest {

    private fun collection(id: Int, isDefault: Boolean = false) =
        MediaCollection(id, if (isDefault) "Favourites" else "List $id", Visibility.Private, "2026-10-14", 0, true, isDefault)

    private val mine = MyCollections(
        collections = listOf(collection(1, isDefault = true), collection(2), collection(3)),
        memberships = mapOf("0:550" to setOf(1, 2), "2:abc" to setOf(3)),
        isLoaded = true,
    )

    @Test
    fun favourite_isMembershipOfDefaultCollection() {
        assertTrue(mine.isFavourite("0:550"))
        assertFalse(mine.isFavourite("2:abc"))
        assertFalse(mine.isFavourite("1:999"))
        assertFalse(MyCollections().isFavourite("0:550"))
    }

    @Test
    fun withMembership_addsAndRemoves() {
        val added = mine.withMembership("1:999", 2, inCollection = true)
        assertTrue(added.contains("1:999", 2))

        val removed = mine.withMembership("0:550", 1, inCollection = false)
        assertFalse(removed.isFavourite("0:550"))
        assertTrue(removed.contains("0:550", 2))

        // The last membership of a media drops its key
        val emptied = mine.withMembership("2:abc", 3, inCollection = false)
        assertFalse("2:abc" in emptied.memberships)
    }

    @Test
    fun withoutCollection_dropsItsMemberships() {
        val result = mine.withoutCollection(3)
        assertEquals(listOf(1, 2), result.collections.map { it.id })
        assertEquals(mapOf("0:550" to setOf(1, 2)), result.memberships)
    }

    @Test
    fun moved_shiftsItemsLikeTheApiReorder() {
        val list = listOf("a", "b", "c", "d")
        assertEquals(listOf("b", "c", "a", "d"), list.moved(0, 2))
        assertEquals(listOf("d", "a", "b", "c"), list.moved(3, 0))
        assertSame(list, list.moved(1, 1))
        assertSame(list, list.moved(1, 9))
    }
}
