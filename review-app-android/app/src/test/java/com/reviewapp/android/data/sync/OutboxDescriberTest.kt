package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.EntityRefDto
import com.reviewapp.android.data.remote.dto.FollowSetPayload
import com.reviewapp.android.data.remote.dto.MediaKeyDto
import com.reviewapp.android.data.remote.dto.ProfileFieldsDto
import com.reviewapp.android.data.remote.dto.ProfileUpdatePayload
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import kotlinx.serialization.KSerializer
import org.junit.Assert.assertEquals
import org.junit.Test

class OutboxDescriberTest {

    private fun <T> op(type: String, entityId: Int?, serializer: KSerializer<T>?, payload: T?, state: Int = OutboxState.PENDING) = OutboxEntity(
        seq = 1,
        opId = "op",
        type = type,
        entityType = entityTypeOf(type),
        entityId = entityId,
        clientRef = null,
        dependsOn = null,
        payloadJson = if (serializer != null && payload != null) OpJson.encodeToString(serializer, payload) else "{}",
        state = state,
        createdAt = 0,
    )

    private val context = DescribeContext(
        mediaTitles = mapOf("0:438631" to "Dune"),
        reviewTitles = mapOf(5 to "Alien"),
        collectionNames = mapOf(3 to "Watch later"),
        usernames = mapOf(20 to "anna"),
    )

    private fun describe(op: OutboxEntity) = OutboxDescriber.describe(op, context)

    @Test
    fun changes_ReadLikeWhatTheUserDid() {
        val dune = MediaKeyDto(0, "438631")
        assertEquals("Wrote a review of Dune", describe(op(OpTypes.REVIEW_CREATE, -1, ReviewCreatePayload.serializer(), ReviewCreatePayload(dune, ReviewFieldsDto(8.0, visibility = 1)))))
        assertEquals("Edited your review of Alien", describe(op<Unit>(OpTypes.REVIEW_UPDATE, 5, null, null)))
        assertEquals("Deleted your review of Alien", describe(op<Unit>(OpTypes.REVIEW_DELETE, 5, null, null)))
        assertEquals("Created the collection Trip", describe(op(OpTypes.COLLECTION_CREATE, -2, CollectionCreatePayload.serializer(), CollectionCreatePayload(CollectionFieldsDto("Trip", 0)))))
        assertEquals("Added Dune to Watch later", describe(op(OpTypes.COLLECTION_ADD_ITEM, 3, CollectionItemPayload.serializer(), CollectionItemPayload(EntityRefDto(id = 3), dune))))
        assertEquals("Removed Dune from Watch later", describe(op(OpTypes.COLLECTION_REMOVE_ITEM, 3, CollectionItemPayload.serializer(), CollectionItemPayload(EntityRefDto(id = 3), dune))))
        assertEquals("Reordered Watch later", describe(op<Unit>(OpTypes.COLLECTION_REORDER, 3, null, null)))
        assertEquals("Updated your bio", describe(op(OpTypes.PROFILE_UPDATE, 7, ProfileUpdatePayload.serializer(), ProfileUpdatePayload(ProfileFieldsDto(bio = "Hi")))))
        assertEquals("Followed anna", describe(op(OpTypes.FOLLOW_SET, 20, FollowSetPayload.serializer(), FollowSetPayload(20, true))))
        assertEquals("Removed anna from your followers", describe(op<Unit>(OpTypes.FOLLOWER_REMOVE, 20, null, null)))
        assertEquals("Marked all notifications as seen", describe(op<Unit>(OpTypes.NOTIFICATION_MARK_ALL_SEEN, null, null, null)))
    }

    @Test
    fun unknownNames_FallBackToGenericWords() {
        assertEquals("Edited your review of a title", describe(op<Unit>(OpTypes.REVIEW_UPDATE, 99, null, null)))
        assertEquals("Unfollowed a user", describe(op(OpTypes.FOLLOW_SET, 99, FollowSetPayload.serializer(), FollowSetPayload(99, false))))
        assertEquals("A change", describe(op<Unit>(OpTypes.COLLECTION_UPDATE, 3, null, null)))
    }

    @Test
    fun status_SaysWhereTheChangeIs() {
        val op = op<Unit>(OpTypes.REVIEW_UPDATE, 5, null, null)
        assertEquals("Waiting to sync", OutboxDescriber.status(op, 0))
        assertEquals("Sending…", OutboxDescriber.status(op.copy(state = OutboxState.IN_FLIGHT), 0))
        assertEquals(
            "Needs your attention: It was changed elsewhere too.",
            OutboxDescriber.status(op.copy(state = OutboxState.CONFLICT, lastReason = "field_conflict"), 0),
        )
        assertEquals(
            "Couldn't be synced: This title is no longer available.",
            OutboxDescriber.status(op.copy(state = OutboxState.REJECTED, lastReason = "media_not_found"), 0),
        )
        assertEquals(
            "Trying again in 2 minutes. The server was busy.",
            OutboxDescriber.status(op.copy(attempts = 1, nextAttemptAt = 150_000, lastReason = "busy"), 30_000),
        )
    }
}
