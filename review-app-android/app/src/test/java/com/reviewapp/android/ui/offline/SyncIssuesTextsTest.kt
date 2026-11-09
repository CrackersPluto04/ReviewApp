package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncMediaDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.sync.DescribeContext
import com.reviewapp.android.data.sync.IssueDetails
import com.reviewapp.android.data.sync.IssueKind
import com.reviewapp.android.data.sync.OpJson
import com.reviewapp.android.data.sync.OpTypes
import com.reviewapp.android.data.sync.PushOutcome
import com.reviewapp.android.data.sync.entityTypeOf
import com.reviewapp.android.domain.model.AchievementTier
import com.reviewapp.android.domain.model.UnlockedAchievement
import com.reviewapp.android.ui.achievements.unlockBannerText
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// The sync summary, the grouped unlock banner and what the conflict screen offers per kind of issue
class SyncIssuesTextsTest {

    @Test
    fun summary_OnlyForChangesThatWaited() {
        // Made online and synced at once: nothing to say
        assertNull(syncSummaryMessage(PushOutcome.Done(applied = 1, issues = 0)))

        assertEquals(
            SyncSummaryMessage("Synced 7 changes. You unlocked 1 achievement.", needsReview = false),
            syncSummaryMessage(PushOutcome.Done(applied = 7, issues = 0, waited = 7, unlocked = 1)),
        )
    }

    @Test
    fun summary_MentionsIssues_AndOverwrittenProfileValues() {
        val message = syncSummaryMessage(
            PushOutcome.Done(applied = 2, issues = 1, waited = 2, waitedIssues = 1, overwritten = listOf("bio", "profilePictureUrl")),
        )!!

        assertEquals(
            "Synced 2 changes. Your bio and profile picture replaced the ones changed elsewhere. 1 change needs your attention.",
            message.text,
        )
        assertTrue(message.needsReview)
        assertEquals("2 changes need your attention.", syncSummaryMessage(PushOutcome.Done(0, 2, waitedIssues = 2))!!.text)
    }

    @Test
    fun unlockBanner_GroupsTiersThatCameTogether() {
        val critic = UnlockedAchievement("critic", "Critic", AchievementTier.Bronze)
        val curator = UnlockedAchievement("curator", "Curator", AchievementTier.Silver)

        assertEquals("Achievement unlocked!" to "Critic (Bronze)", unlockBannerText(listOf(critic)))
        assertEquals("2 achievements unlocked!" to "Critic (Bronze), Curator (Silver)", unlockBannerText(listOf(critic, curator)))
    }

    // The conflict screen

    private fun op(type: String, entityId: Int, state: Int = OutboxState.CONFLICT, reason: String? = null, payload: String = "{}") = OutboxEntity(
        seq = 3, opId = "op3", type = type, entityType = entityTypeOf(type), entityId = entityId, clientRef = "op3".takeIf { type.endsWith(".create") },
        dependsOn = null, payloadJson = payload, state = state, lastReason = reason, createdAt = 0,
    )

    private val dune = SyncMediaDto(1, 0, "438631", "Dune")
    private val mine = ReviewFieldsDto(9.0, "Loved it", null, null, 1)
    private val theirs = SyncReviewDto(12, dune, 6.0, "Too long", visibility = 0, version = 40)
    private val row = ReviewEntity(12, 0, "438631", 9.0, "Loved it", null, null, 1, "", "", 30)
    private val context = DescribeContext(mediaTitles = mapOf("0:438631" to "Dune"), reviewTitles = mapOf(12 to "Dune"))

    @Test
    fun fieldConflict_OneChoicePerField_AndDiscardKeepsTheirs() {
        val details = IssueDetails(
            op(OpTypes.REVIEW_UPDATE, 12), IssueKind.FieldConflict,
            SyncOpResultDto("op3", "conflict", "field_conflict", conflictFields = listOf("score", "reviewText")),
            localReview = row, serverReview = theirs, mineReview = mine,
        )

        val view = issueView(details, context)

        assertEquals("Edited your review of Dune", view.title)
        assertEquals(listOf("score", "reviewText"), view.diffs.map { it.field })
        assertEquals(listOf(IssueAction.SaveChoices, IssueAction.UseTheirs), view.actions)
    }

    @Test
    fun deletedElsewhere_RecreateOnlyWhileThisPhoneStillHasIt() {
        val details = IssueDetails(op(OpTypes.REVIEW_UPDATE, 12), IssueKind.DeletedElsewhere, null, localReview = row, mineReview = mine)

        assertEquals(listOf(IssueAction.Recreate, IssueAction.UseTheirs), issueView(details, context).actions)
        assertEquals(listOf(IssueAction.UseTheirs), issueView(details.copy(localReview = null), context).actions)
        assertEquals(listOf(IssueAction.UseTheirs), issueView(details.copy(deletedHereLater = true), context).actions)
    }

    @Test
    fun alreadyReviewed_ShowsBothVersions() {
        val details = IssueDetails(
            op(OpTypes.REVIEW_CREATE, -1), IssueKind.AlreadyReviewed, null,
            localReview = row.copy(id = -1), serverReview = theirs, mineReview = mine,
        )

        val view = issueView(details, context)

        assertEquals(ReviewVersion("9", "Public", "Loved it"), view.mine)
        assertEquals(ReviewVersion("6", "Private", "Too long"), view.theirs)
        assertEquals(listOf(IssueAction.KeepMine, IssueAction.UseTheirs), view.actions)
        assertEquals("Keep the other one", view.discardLabel)
    }

    @Test
    fun nameTaken_RenameAlways_MergeOnlyForACollectionMadeHere() {
        val create = op(OpTypes.COLLECTION_CREATE, -2, payload = OpJson.encodeToString(CollectionCreatePayload.serializer(), CollectionCreatePayload(CollectionFieldsDto("Watch later", 1))))
        val existing = SyncCollectionDto(30, "Watch later", 1)
        val details = IssueDetails(
            create, IssueKind.NameTaken, null,
            localCollection = CollectionEntity(-2, "Watch later", 1, false, "", "", null),
            serverCollection = existing, mineCollection = CollectionFieldsDto("Watch later", 1),
        )

        val view = issueView(details, context)
        assertEquals("Watch later", view.currentName)
        assertEquals(listOf(IssueAction.Rename, IssueAction.Merge, IssueAction.UseTheirs), view.actions)

        val rename = details.copy(op = op(OpTypes.COLLECTION_UPDATE, 8))
        assertEquals(listOf(IssueAction.Rename, IssueAction.UseTheirs), issueView(rename, context).actions)
    }

    @Test
    fun rejected_RetryOnlyWhenTheServerKeptFailing() {
        val failing = IssueDetails(op(OpTypes.REVIEW_UPDATE, 12, OutboxState.REJECTED, "server_error"), IssueKind.Rejected, null)
        assertEquals(listOf(IssueAction.Retry, IssueAction.UseTheirs), issueView(failing, context).actions)

        val gone = IssueDetails(op(OpTypes.REVIEW_CREATE, -1, OutboxState.REJECTED, "media_not_found"), IssueKind.Rejected, null)
        val view = issueView(gone, context)
        assertEquals(listOf(IssueAction.UseTheirs), view.actions)
        assertTrue(view.explanation, view.explanation.endsWith("This title is no longer available."))
        assertFalse(view.actions.contains(IssueAction.Retry))
    }

}
