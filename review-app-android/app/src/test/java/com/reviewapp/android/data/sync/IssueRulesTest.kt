package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Sync issues: what kind of problem a change ran into, and how the user's choices become the change sent again
class IssueRulesTest {

    private fun op(state: Int, reason: String?, type: String = OpTypes.REVIEW_UPDATE) = OutboxEntity(
        seq = 1, opId = "op1", type = type, entityType = EntityType.REVIEW, entityId = 5, clientRef = null,
        dependsOn = null, payloadJson = "{}", state = state, lastReason = reason, createdAt = 0,
    )

    private fun answer(reason: String) = SyncOpResultDto("op1", "conflict", reason)

    @Test
    fun kind_FollowsTheBackendsReason() {
        val conflict = op(OutboxState.CONFLICT, null)
        assertEquals(IssueKind.FieldConflict, IssueRules.kindOf(conflict, answer("field_conflict")))
        assertEquals(IssueKind.DeletedElsewhere, IssueRules.kindOf(conflict, answer("deleted")))
        assertEquals(IssueKind.DeletedElsewhere, IssueRules.kindOf(conflict, answer("collection_deleted")))
        assertEquals(IssueKind.ModifiedSince, IssueRules.kindOf(conflict, answer("modified_since")))
        assertEquals(IssueKind.AlreadyReviewed, IssueRules.kindOf(conflict, answer("already_reviewed")))
        assertEquals(IssueKind.NameTaken, IssueRules.kindOf(conflict, answer("name_taken")))
        // A conflict this app version doesn't know, and every rejection, can only be discarded
        assertEquals(IssueKind.Rejected, IssueRules.kindOf(conflict, answer("something_new")))
        assertEquals(IssueKind.Rejected, IssueRules.kindOf(op(OutboxState.REJECTED, "locked"), answer("locked")))
        // Without the stored answer the op's own reason decides
        assertEquals(IssueKind.NameTaken, IssueRules.kindOf(op(OutboxState.CONFLICT, "name_taken"), null))
    }

    @Test
    fun onlyRejectionsOfAFailingServer_CanBeTriedAgain() {
        assertTrue(IssueRules.canRetry(op(OutboxState.REJECTED, "server_error")))
        assertFalse(IssueRules.canRetry(op(OutboxState.REJECTED, "media_not_found")))
        assertFalse(IssueRules.canRetry(op(OutboxState.CONFLICT, "server_error")))
    }

    private val base = ReviewFieldsDto(7.0, "Good", "Cast", null, 1)

    @Test
    fun diffs_OnlyTheConflictingFields_InTheFormsOrder() {
        val mine = base.copy(score = 8.5, reviewText = "Great")
        val theirs = base.copy(score = 6.0, reviewText = "Meh")

        val diffs = IssueRules.reviewDiffs(mine, theirs, listOf("reviewText", "score"))

        assertEquals(listOf("score", "reviewText"), diffs.map { it.field })
        assertEquals("8.5", diffs[0].mine)
        assertEquals("6", diffs[0].theirs)
        assertFalse(diffs[0].isText)
        assertTrue(diffs[1].isText)
        assertEquals("Great\n\nMeh", diffs[1].combined)
    }

    @Test
    fun resolve_KeepsWhatOnlyIChanged_TakesWhatOnlyTheyChanged_AndAppliesTheChoices() {
        // Mine: score and text; theirs: text and pros. Text is the conflict.
        val mine = base.copy(score = 9.0, reviewText = "Loved it")
        val theirs = base.copy(reviewText = "Liked it", pros = "Music")

        val keepMine = IssueRules.resolveReview(base, mine, theirs, mapOf("reviewText" to FieldChoice.Mine))
        assertEquals(ReviewFieldsDto(9.0, "Loved it", "Music", null, 1), keepMine)

        val takeTheirs = IssueRules.resolveReview(base, mine, theirs, mapOf("reviewText" to FieldChoice.Theirs))
        assertEquals("Liked it", takeTheirs.reviewText)
        assertEquals(9.0, takeTheirs.score, 0.0)

        val edited = IssueRules.resolveReview(base, mine, theirs, mapOf("reviewText" to FieldChoice.Edited("  Loved and liked  ")))
        assertEquals("Loved and liked", edited.reviewText)
        // An emptied text is no text
        assertNull(IssueRules.resolveReview(base, mine, theirs, mapOf("reviewText" to FieldChoice.Edited(" "))).reviewText)
    }

    @Test
    fun resolve_WithoutABase_IsMine() {
        val mine = base.copy(score = 9.0)
        assertEquals(mine, IssueRules.resolveReview(null, mine, base.copy(score = 2.0, cons = "Long"), emptyMap()))
    }

    @Test
    fun resolve_TreatsAnEmptyTextLikeNoText() {
        // Not changed by me ("" vs null), changed by them: theirs
        val resolved = IssueRules.resolveReview(base.copy(cons = null), base.copy(cons = ""), base.copy(cons = "Slow"), emptyMap())
        assertEquals("Slow", resolved.cons)
    }

    @Test
    fun resolveCollection_NameAndVisibility() {
        val baseFields = CollectionFieldsDto("Watch later", 1)
        val resolved = IssueRules.resolveCollection(
            baseFields,
            mine = CollectionFieldsDto("Watch soon", 0),
            theirs = CollectionFieldsDto("To watch", 1),
            choices = mapOf("name" to FieldChoice.Edited(" Watch next ")),
        )
        assertEquals(CollectionFieldsDto("Watch next", 0), resolved)
        assertEquals(
            listOf("name"),
            IssueRules.collectionDiffs(CollectionFieldsDto("A", 0), CollectionFieldsDto("B", 0), listOf("name")).map { it.field },
        )
    }

    @Test
    fun combine_StartsFromBothTexts_WithinTheLimit() {
        assertEquals("Theirs", IssueRules.combine("", "Theirs", 500))
        assertEquals("Mine", IssueRules.combine("Mine", " ", 500))
        assertEquals("Same", IssueRules.combine("Same", "Same ", 500))
        assertEquals("Mine\n\nThe", IssueRules.combine("Mine", "Theirs", 9))
    }

    @Test
    fun retarget_PointsTheCreatesRefAtTheRealId_AndLeavesTheRest() {
        val payload = """{"collection":{"ref":"abc"},"media":{"type":0,"externalApiId":"603"}}"""

        val retargeted = OpJson.parseToJsonElement(IssueRules.retarget(payload, "abc", 42))

        assertEquals(OpJson.parseToJsonElement("""{"collection":{"id":42},"media":{"type":0,"externalApiId":"603"}}"""), retargeted)
        // Another create's ref stays
        assertEquals(OpJson.parseToJsonElement(payload), OpJson.parseToJsonElement(IssueRules.retarget(payload, "other", 42)))
    }

    @Test
    fun withoutDependency_DropsOnlyThatOp() {
        assertEquals("b", IssueRules.withoutDependency("a,b", "a"))
        assertNull(IssueRules.withoutDependency("a", "a"))
        assertNull(IssueRules.withoutDependency(null, "a"))
    }
}
