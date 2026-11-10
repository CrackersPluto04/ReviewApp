package com.reviewapp.android.ui.offline

import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.sync.DescribeContext
import com.reviewapp.android.data.sync.FieldDiff
import com.reviewapp.android.data.sync.IssueDetails
import com.reviewapp.android.data.sync.IssueKind
import com.reviewapp.android.data.sync.IssueRules
import com.reviewapp.android.data.sync.OpTypes
import com.reviewapp.android.data.sync.OutboxDescriber
import com.reviewapp.android.data.sync.entityTypeOf
import com.reviewapp.android.data.sync.fields
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.util.formatScore

/** A decision the conflict screen offers. */
enum class IssueAction(val label: String) {
    /** Field conflict: send the per-field choices on top of the backend's version. */
    SaveChoices("Save my choices"),

    /** Deleted elsewhere: create it again from what this phone shows. */
    Recreate("Recreate it"),

    /** Edited elsewhere since: delete it anyway. */
    DeleteAnyway("Delete anyway"),

    /** Already reviewed: this phone's review replaces the one on the backend. */
    KeepMine("Keep mine"),

    /** Name taken: pick another name for the collection. */
    Rename("Rename"),

    /** Name taken: move what was put in the new collection into the existing one. */
    Merge("Add to the existing collection"),

    /** Rejected: send the change again. */
    Retry("Try again"),

    /** The backend's version stays: the change (and the later ones of the same thing) is thrown away. */
    UseTheirs("Discard my change"),
}

/** A review as one of the two versions shows it. */
data class ReviewVersion(val score: String, val visibility: String, val text: String?)

/** What the conflict screen shows for one change in Sync issues (pure, unit-tested). */
data class IssueView(
    val title: String,
    val explanation: String,
    /** Field conflicts: one choice per field. */
    val diffs: List<FieldDiff> = emptyList(),
    /** Two versions side by side (already reviewed, edited since). */
    val mine: ReviewVersion? = null,
    val theirs: ReviewVersion? = null,
    /** A taken name: the field for another one. */
    val currentName: String? = null,
    /** The first is the main one; [IssueAction.UseTheirs] is always last. */
    val actions: List<IssueAction>,
    /** The label of [IssueAction.UseTheirs] here. */
    val discardLabel: String = IssueAction.UseTheirs.label,
)

/**
 * Picks the texts and the offered actions for one issue. Only the actions that can work are offered: recreating or
 * keeping mine needs the local row to still exist, merging needs the existing collection, retrying needs a failure
 * that can pass.
 */
fun issueView(details: IssueDetails, context: DescribeContext): IssueView {
    val op = details.op
    val title = OutboxDescriber.describe(op, context)
    val isReview = entityTypeOf(op.type) == EntityType.REVIEW
    val what = when {
        isReview -> "your review of ${details.localReview?.let { context.reviewTitles[it.id] } ?: details.serverReview?.media?.title ?: op.entityId?.let { context.reviewTitles[it] } ?: "this title"}"
        entityTypeOf(op.type) == EntityType.COLLECTION -> "the collection ${details.mineCollection?.name ?: details.serverCollection?.name ?: "you changed"}"
        else -> "it"
    }
    val canUseRow = !details.deletedHereLater && (details.localReview != null || details.localCollection != null)

    return when (details.kind) {
        IssueKind.FieldConflict -> {
            val fields = details.answer?.conflictFields.orEmpty()
            val diffs = when {
                isReview && details.mineReview != null && details.serverReview != null ->
                    IssueRules.reviewDiffs(details.mineReview, details.serverReview.fields(), fields)
                details.mineCollection != null && details.serverCollection != null ->
                    IssueRules.collectionDiffs(details.mineCollection, CollectionFieldsDto(details.serverCollection.name, details.serverCollection.visibility), fields)
                else -> emptyList()
            }
            IssueView(
                title,
                "You changed $what on this phone, and it was also changed elsewhere. Choose which version to keep where they differ; your other changes are kept.",
                diffs = diffs,
                actions = listOfNotNull(IssueAction.SaveChoices.takeIf { diffs.isNotEmpty() }, IssueAction.UseTheirs),
                discardLabel = "Use the other version everywhere",
            )
        }
        IssueKind.DeletedElsewhere -> IssueView(
            title,
            "${what.replaceFirstChar(Char::uppercase)} was deleted elsewhere after you changed it on this phone." +
                if (canUseRow) " You can create it again as this phone shows it, or let it go." else " It can only be discarded.",
            actions = listOfNotNull(IssueAction.Recreate.takeIf { canUseRow }, IssueAction.UseTheirs),
            discardLabel = "Discard my change",
        )
        IssueKind.ModifiedSince -> IssueView(
            title,
            "You deleted $what on this phone, but it was edited elsewhere since. Delete it anyway, or keep the edited one?",
            theirs = details.serverReview?.fields()?.toVersion(),
            actions = listOf(IssueAction.DeleteAnyway, IssueAction.UseTheirs),
            discardLabel = "Keep it",
        )
        IssueKind.AlreadyReviewed -> IssueView(
            title,
            "You reviewed this title on this phone, but you had also reviewed it elsewhere meanwhile. You can have one review per title: which one stays?",
            mine = details.mineReview?.toVersion(),
            theirs = details.serverReview?.fields()?.toVersion(),
            actions = listOfNotNull(IssueAction.KeepMine.takeIf { canUseRow && details.serverReview != null }, IssueAction.UseTheirs),
            discardLabel = "Keep the other one",
        )
        IssueKind.NameTaken -> {
            val name = details.mineCollection?.name.orEmpty()
            val canMerge = op.type == OpTypes.COLLECTION_CREATE && !details.deletedHereLater && details.serverCollection != null
            IssueView(
                title,
                "You already have a collection named \"$name\". Pick another name" +
                    (if (canMerge) ", or add what you put in this one to that collection." else "."),
                currentName = name,
                actions = listOfNotNull(IssueAction.Rename, IssueAction.Merge.takeIf { canMerge }, IssueAction.UseTheirs),
                discardLabel = "Discard my change",
            )
        }
        IssueKind.Rejected -> IssueView(
            title,
            "This change couldn't be synced. ${OutboxDescriber.reasonMessage(op.lastReason)}",
            actions = listOfNotNull(IssueAction.Retry.takeIf { IssueRules.canRetry(op) }, IssueAction.UseTheirs),
            discardLabel = "Discard",
        )
    }
}

/** A review's fields as the readable [ReviewVersion] of the side-by-side view. */
private fun ReviewFieldsDto.toVersion() = ReviewVersion(
    score = formatScore(score),
    visibility = Visibility.fromId(visibility).label,
    text = reviewText?.takeIf { it.isNotBlank() },
)
