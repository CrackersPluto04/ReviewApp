package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.CollectionReorderPayload
import com.reviewapp.android.data.remote.dto.CollectionUpdatePayload
import com.reviewapp.android.data.remote.dto.EntityRefDto
import com.reviewapp.android.data.remote.dto.FollowSetPayload
import com.reviewapp.android.data.remote.dto.MediaKeyDto
import com.reviewapp.android.data.remote.dto.NotificationDeletePayload
import com.reviewapp.android.data.remote.dto.NotificationSeenPayload
import com.reviewapp.android.data.remote.dto.NotificationUpToPayload
import com.reviewapp.android.data.remote.dto.ProfileFieldsDto
import com.reviewapp.android.data.remote.dto.ProfileUpdatePayload
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.data.remote.dto.ReviewDeletePayload
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewUpdatePayload
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import kotlinx.serialization.KSerializer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// The pure rules of the offline queue: combining changes, choosing the next push, retrying, reading answers
class OutboxRulesTest {

    private var nextSeq = 1L

    private fun <T> op(
        type: String,
        entityId: Int?,
        serializer: KSerializer<T>,
        payload: T,
        state: Int = OutboxState.PENDING,
        dependsOn: String? = null,
        nextAttemptAt: Long = 0,
        attempts: Int = 0,
    ) = OutboxEntity(
        seq = nextSeq++,
        opId = "op${nextSeq}",
        type = type,
        entityType = entityTypeOf(type),
        entityId = entityId,
        clientRef = if (type.endsWith(".create")) "ref$entityId" else null,
        dependsOn = dependsOn,
        payloadJson = OpJson.encodeToString(serializer, payload),
        state = state,
        attempts = attempts,
        nextAttemptAt = nextAttemptAt,
        createdAt = 0,
    )

    private fun fields(score: Double, text: String? = null) = ReviewFieldsDto(score, text, null, null, 1)
    private val dune = MediaKeyDto(0, "438631")
    private val alien = MediaKeyDto(0, "348")

    private fun reviewCreate(id: Int, score: Double = 7.0) =
        op(OpTypes.REVIEW_CREATE, id, ReviewCreatePayload.serializer(), ReviewCreatePayload(dune, fields(score)))
    private fun reviewUpdate(id: Int, score: Double, baseVersion: Long? = 10, base: Double = 5.0, state: Int = OutboxState.PENDING) =
        op(OpTypes.REVIEW_UPDATE, id, ReviewUpdatePayload.serializer(), ReviewUpdatePayload(EntityRefDto(id = id), baseVersion, fields(base), fields(score)), state)
    private fun reviewDelete(id: Int, baseVersion: Long? = 10) =
        op(OpTypes.REVIEW_DELETE, id, ReviewDeletePayload.serializer(), ReviewDeletePayload(EntityRefDto(id = id), baseVersion))
    private fun item(type: String, collection: Int, media: MediaKeyDto, dependsOn: String? = null) =
        op(type, collection, CollectionItemPayload.serializer(), CollectionItemPayload(EntityRefDto(id = collection), media), dependsOn = dependsOn)

    private fun <T> payload(op: OutboxEntity, serializer: KSerializer<T>): T = OpJson.decodeFromString(serializer, op.payloadJson)

    // Combining

    @Test
    fun editsOfAReviewNotSentYet_GoIntoItsCreate() {
        val create = reviewCreate(-1)
        val result = OutboxCompactor.add(listOf(create), reviewUpdate(-1, score = 9.0))

        assertNull(result.insert)
        assertEquals(9.0, payload(result.update.single(), ReviewCreatePayload.serializer()).fields.score, 0.0)
    }

    @Test
    fun aSecondEdit_KeepsTheFirstBase_AndTakesTheLatestValues() {
        val first = reviewUpdate(5, score = 7.0, baseVersion = 10, base = 5.0)
        val result = OutboxCompactor.add(listOf(first), reviewUpdate(5, score = 9.0, baseVersion = 99, base = 7.0))

        val merged = payload(result.update.single(), ReviewUpdatePayload.serializer())
        assertEquals(9.0, merged.fields.score, 0.0)
        assertEquals(5.0, merged.base!!.score, 0.0)
        assertEquals(10L, merged.baseVersion)
    }

    @Test
    fun anEditAfterOneBeingSent_IsANewOp() {
        // The first may already be applied: it is never changed
        val sending = reviewUpdate(5, score = 7.0, state = OutboxState.IN_FLIGHT)
        val next = reviewUpdate(5, score = 9.0)

        assertEquals(next, OutboxCompactor.add(listOf(sending), next).insert)
    }

    @Test
    fun createdAndDeletedBeforeASync_NothingIsSent() {
        val create = reviewCreate(-1)
        val edit = reviewUpdate(-1, 8.0)
        val result = OutboxCompactor.add(listOf(create), reviewDelete(-1, baseVersion = null))

        assertEquals(listOf(create.seq), result.delete)
        assertNull(result.insert)
        // An edit folded in before goes too
        assertEquals(setOf(create.seq, edit.seq), OutboxCompactor.add(listOf(create, edit), reviewDelete(-1)).delete.toSet())
    }

    @Test
    fun deletingAnEditedReview_DropsTheEdit_AndKeepsItsBaseVersion() {
        val edit = reviewUpdate(5, 8.0, baseVersion = 10)
        val result = OutboxCompactor.add(listOf(edit), reviewDelete(5, baseVersion = 11))

        assertEquals(listOf(edit.seq), result.delete)
        assertEquals(10L, payload(result.insert!!, ReviewDeletePayload.serializer()).baseVersion)
    }

    @Test
    fun collectionCreatedAndDeleted_TakesItsItemsAlong() {
        val create = op(OpTypes.COLLECTION_CREATE, -2, CollectionCreatePayload.serializer(), CollectionCreatePayload(CollectionFieldsDto("Trip", 0)))
        val add = item(OpTypes.COLLECTION_ADD_ITEM, -2, dune, dependsOn = create.opId)
        val delete = op(OpTypes.COLLECTION_DELETE, -2, com.reviewapp.android.data.remote.dto.CollectionDeletePayload.serializer(),
            com.reviewapp.android.data.remote.dto.CollectionDeletePayload(EntityRefDto(ref = "ref-2")))

        val result = OutboxCompactor.add(listOf(create, add), delete)

        assertEquals(setOf(create.seq, add.seq), result.delete.toSet())
        assertNull(result.insert)
    }

    @Test
    fun aRenameOfACollectionNotSentYet_GoesIntoItsCreate() {
        val create = op(OpTypes.COLLECTION_CREATE, -2, CollectionCreatePayload.serializer(), CollectionCreatePayload(CollectionFieldsDto("Trip", 0)))
        val rename = op(OpTypes.COLLECTION_UPDATE, -2, CollectionUpdatePayload.serializer(),
            CollectionUpdatePayload(EntityRefDto(ref = "ref-2"), null, null, CollectionFieldsDto("Summer trip", 1)))

        val result = OutboxCompactor.add(listOf(create), rename)

        assertEquals(CollectionFieldsDto("Summer trip", 1), payload(result.update.single(), CollectionCreatePayload.serializer()).fields)
    }

    @Test
    fun addingAndRemovingTheSameMedia_CancelOut() {
        val add = item(OpTypes.COLLECTION_ADD_ITEM, 3, dune)
        val other = item(OpTypes.COLLECTION_ADD_ITEM, 3, alien)

        val removed = OutboxCompactor.add(listOf(add, other), item(OpTypes.COLLECTION_REMOVE_ITEM, 3, dune))
        assertEquals(listOf(add.seq), removed.delete)
        assertNull(removed.insert)

        // Adding it twice is one add
        assertEquals(Compaction(), OutboxCompactor.add(listOf(add), item(OpTypes.COLLECTION_ADD_ITEM, 3, dune)))
    }

    @Test
    fun onlyTheLastReorderIsSent_AfterTheAdds() {
        val first = op(OpTypes.COLLECTION_REORDER, 3, CollectionReorderPayload.serializer(), CollectionReorderPayload(EntityRefDto(id = 3), listOf(dune, alien)))
        val second = op(OpTypes.COLLECTION_REORDER, 3, CollectionReorderPayload.serializer(), CollectionReorderPayload(EntityRefDto(id = 3), listOf(alien, dune)))

        val result = OutboxCompactor.add(listOf(first), second)

        assertEquals(listOf(first.seq), result.delete)
        assertEquals(second, result.insert)
    }

    @Test
    fun profileEdits_Merge_FieldByField() {
        val bio = op(OpTypes.PROFILE_UPDATE, 7, ProfileUpdatePayload.serializer(), ProfileUpdatePayload(ProfileFieldsDto(bio = "Hi"), ProfileFieldsDto(bio = "", profilePictureUrl = "")))
        val avatar = op(OpTypes.PROFILE_UPDATE, 7, ProfileUpdatePayload.serializer(), ProfileUpdatePayload(ProfileFieldsDto(profilePictureUrl = "https://x/a.png"), ProfileFieldsDto(bio = "Hi")))

        val merged = payload(OutboxCompactor.add(listOf(bio), avatar).update.single(), ProfileUpdatePayload.serializer())

        assertEquals(ProfileFieldsDto(bio = "Hi", profilePictureUrl = "https://x/a.png"), merged.fields)
        assertEquals("", merged.base!!.bio)
    }

    @Test
    fun followAndNotificationStates_TheLatestIntentWins() {
        val follow = op(OpTypes.FOLLOW_SET, 20, FollowSetPayload.serializer(), FollowSetPayload(20, true))
        val unfollow = op(OpTypes.FOLLOW_SET, 20, FollowSetPayload.serializer(), FollowSetPayload(20, false))
        assertEquals(Compaction(delete = listOf(follow.seq), insert = unfollow), OutboxCompactor.add(listOf(follow), unfollow))

        val seen = op(OpTypes.NOTIFICATION_SEEN, 4, NotificationSeenPayload.serializer(), NotificationSeenPayload(4, true))
        val delete = op(OpTypes.NOTIFICATION_DELETE, 4, NotificationDeletePayload.serializer(), NotificationDeletePayload(4))
        assertEquals(Compaction(delete = listOf(seen.seq), insert = delete), OutboxCompactor.add(listOf(seen), delete))
    }

    @Test
    fun markAllSeenTwice_IsOneOp_UpToTheNewest() {
        val first = op(OpTypes.NOTIFICATION_MARK_ALL_SEEN, null, NotificationUpToPayload.serializer(), NotificationUpToPayload(10))
        val second = op(OpTypes.NOTIFICATION_MARK_ALL_SEEN, null, NotificationUpToPayload.serializer(), NotificationUpToPayload(15))

        assertEquals(15, payload(OutboxCompactor.add(listOf(first), second).update.single(), NotificationUpToPayload.serializer()).upToId)
    }

    @Test
    fun changesOfDifferentThings_AreNeverCombined() {
        val other = reviewUpdate(6, 8.0)
        val mine = reviewUpdate(5, 9.0)

        assertEquals(mine, OutboxCompactor.add(listOf(other), mine).insert)
    }

    // Choosing the next push

    @Test
    fun aBatch_KeepsTheQueueOrder_UpToTheLimit() {
        val queue = (1..60).map { reviewUpdate(it, 8.0) }

        val batch = SyncPlanner.nextBatch(queue, nowMillis = 0)

        assertEquals(SyncPlanner.MAX_BATCH, batch.size)
        assertEquals(queue.take(SyncPlanner.MAX_BATCH), batch)
    }

    @Test
    fun anOpBehindAnIssueOrARetry_OfTheSameThing_Waits_OthersGo() {
        val conflict = reviewUpdate(5, 8.0, state = OutboxState.CONFLICT)
        val after = reviewDelete(5)
        val retrying = reviewUpdate(6, 8.0).copy(nextAttemptAt = 5_000, attempts = 1)
        val behindRetry = reviewDelete(6)
        val free = reviewUpdate(7, 8.0)

        assertEquals(listOf(free), SyncPlanner.nextBatch(listOf(conflict, after, retrying, behindRetry, free), nowMillis = 1_000))
        // Once its time comes the retry goes, with what follows it
        assertEquals(listOf(retrying, behindRetry, free), SyncPlanner.nextBatch(listOf(conflict, after, retrying, behindRetry, free), nowMillis = 5_000))
    }

    @Test
    fun aDependentOp_GoesInTheSameBatchAsItsDependency_OrWaits() {
        val create = op(OpTypes.COLLECTION_CREATE, -2, CollectionCreatePayload.serializer(), CollectionCreatePayload(CollectionFieldsDto("Trip", 0)))
        val add = item(OpTypes.COLLECTION_ADD_ITEM, -2, dune, dependsOn = create.opId)

        assertEquals(listOf(create, add), SyncPlanner.nextBatch(listOf(create, add), 0))
        // The create in Sync issues: the add can't go
        assertEquals(emptyList<OutboxEntity>(), SyncPlanner.nextBatch(listOf(create.copy(state = OutboxState.CONFLICT), add), 0))
        // The create already applied (not queued any more): the add goes alone
        assertEquals(listOf(add), SyncPlanner.nextBatch(listOf(add), 0))
    }

    @Test
    fun opsBeingSent_AreNotSentAgain() {
        val sending = reviewUpdate(5, 8.0, state = OutboxState.IN_FLIGHT)
        assertEquals(emptyList<OutboxEntity>(), SyncPlanner.nextBatch(listOf(sending), 0))
    }

    // Retrying

    @Test
    fun retries_BackOffExponentially_UpToAnHour_WithJitter() {
        assertEquals(5_000L, RetryPolicy.delayMillis(1, random = 0.5))
        assertEquals(10_000L, RetryPolicy.delayMillis(2, random = 0.5))
        assertEquals(40_000L, RetryPolicy.delayMillis(4, random = 0.5))
        assertEquals(RetryPolicy.MAX_DELAY_MILLIS, RetryPolicy.delayMillis(30, random = 0.5))
        assertEquals(4_000L, RetryPolicy.delayMillis(1, random = 0.0))
        assertEquals(5_999L, RetryPolicy.delayMillis(1, random = 0.9999))
    }

    @Test
    fun aRetryAfter_IsAMinimum() {
        assertEquals(120_000L, RetryPolicy.delayMillis(1, retryAfterSeconds = 120, random = 0.5))
        assertEquals(40_000L, RetryPolicy.delayMillis(4, retryAfterSeconds = 1, random = 0.5))
    }

    // Reading the answers

    private fun answer(status: String, reason: String? = null, entityId: Int? = null) = SyncOpResultDto("x", status, reason, entityId)

    @Test
    fun answers_BecomeOutcomes() {
        val op = reviewUpdate(5, 8.0)
        assertEquals(OpOutcome.Done(42, merged = false), ResultInterpreter.outcome(answer("applied", entityId = 42), op, 0))
        assertEquals(OpOutcome.Done(null, merged = true), ResultInterpreter.outcome(answer("merged"), op, 0))
        assertEquals(OpOutcome.Issue(OutboxState.CONFLICT, "field_conflict"), ResultInterpreter.outcome(answer("conflict", "field_conflict"), op, 0))
        assertEquals(OpOutcome.Issue(OutboxState.REJECTED, "invalid"), ResultInterpreter.outcome(answer("rejected", "invalid"), op, 0))
        assertEquals(OpOutcome.Waiting, ResultInterpreter.outcome(answer("skipped", "dependency"), op, 0))
        assertEquals(OpOutcome.Retry(1_000 + 5_000, 1, "source_unavailable"), ResultInterpreter.outcome(answer("retry", "source_unavailable"), op, 1_000))
    }

    @Test
    fun repeatedServerErrors_EndInSyncIssues() {
        val tired = reviewUpdate(5, 8.0).copy(attempts = RetryPolicy.MAX_SERVER_ERRORS - 1)

        assertEquals(OpOutcome.Issue(OutboxState.REJECTED, "server_error"), ResultInterpreter.outcome(answer("retry", "server_error"), tired, 0))
        // Other temporary failures keep retrying
        assertTrue(ResultInterpreter.outcome(answer("retry", "source_unavailable"), tired, 0) is OpOutcome.Retry)
    }

    @Test
    fun rebase_MovesTheBaseVersionOfWaitingEdits() {
        val edit = reviewUpdate(5, 9.0, baseVersion = 10)

        assertEquals(77L, payload(ResultInterpreter.rebase(edit, 77), ReviewUpdatePayload.serializer()).baseVersion)
        // An op without a base version is left alone
        val add = item(OpTypes.COLLECTION_ADD_ITEM, 3, dune)
        assertEquals(add, ResultInterpreter.rebase(add, 77))
    }

    @Test
    fun opTypes_BelongToTheirEntities() {
        assertEquals(EntityType.REVIEW, entityTypeOf(OpTypes.REVIEW_DELETE))
        assertEquals(EntityType.COLLECTION, entityTypeOf(OpTypes.COLLECTION_REORDER))
        assertEquals(EntityType.FOLLOWER, entityTypeOf(OpTypes.FOLLOWER_REMOVE))
        assertEquals(EntityType.NOTIFICATION, entityTypeOf(OpTypes.NOTIFICATION_DELETE_SEEN))
    }
}
