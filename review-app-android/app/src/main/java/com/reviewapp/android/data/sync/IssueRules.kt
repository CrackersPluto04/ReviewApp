package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.util.Validation
import com.reviewapp.android.util.formatScore
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** What a change in Sync issues ran into, which decides the choices the user gets. */
enum class IssueKind {
    /** The same field was changed here and elsewhere: keep mine / use theirs / edit, per field. */
    FieldConflict,
    /** The review or collection was deleted elsewhere: recreate it or discard. */
    DeletedElsewhere,
    /** A review deleted here was edited elsewhere since: delete anyway or keep it. */
    ModifiedSince,
    /** A review written here, but the title was reviewed elsewhere meanwhile: keep mine or use theirs. */
    AlreadyReviewed,
    /** A collection name that is taken: rename, or (a new one) merge into the existing one. */
    NameTaken,
    /** The backend refused it (or kept failing): it can only be discarded, or tried again. */
    Rejected,
}

/** How the user decided one conflicting field. */
sealed interface FieldChoice {
    data object Mine : FieldChoice
    data object Theirs : FieldChoice
    /** An edited text (only for text fields). */
    data class Edited(val text: String) : FieldChoice
}

/** One field changed differently on both sides, as the conflict screen shows it. */
data class FieldDiff(
    /** The API's field name (`score`, `reviewText`, `pros`, `cons`, `visibility`, `name`). */
    val field: String,
    val label: String,
    val mine: String,
    val theirs: String,
    /** Can be edited into a combination. */
    val isText: Boolean,
    /** Mine and theirs one after the other, the start of an edit. */
    val combined: String = "",
    val maxLength: Int = 0,
)

/** The rules behind Sync issues (pure, unit-tested). */
object IssueRules {

    fun kindOf(op: OutboxEntity, answer: SyncOpResultDto?): IssueKind {
        if (op.state != OutboxState.CONFLICT) return IssueKind.Rejected
        return when (answer?.reason ?: op.lastReason) {
            "field_conflict" -> IssueKind.FieldConflict
            "deleted", "collection_deleted" -> IssueKind.DeletedElsewhere
            "modified_since" -> IssueKind.ModifiedSince
            "already_reviewed" -> IssueKind.AlreadyReviewed
            "name_taken" -> IssueKind.NameTaken
            else -> IssueKind.Rejected
        }
    }

    /** A rejection that may go through when sent again (the server kept failing, not a business rule). */
    fun canRetry(op: OutboxEntity): Boolean = op.state == OutboxState.REJECTED && op.lastReason in RETRYABLE

    private val RETRYABLE = setOf("server_error", "busy", "source_unavailable", "deferred", null)

    // Field conflicts

    fun reviewDiffs(mine: ReviewFieldsDto, theirs: ReviewFieldsDto, conflictFields: List<String>): List<FieldDiff> =
        REVIEW_FIELDS.filter { it in conflictFields }.map { field ->
            when (field) {
                "score" -> FieldDiff(field, "Score", formatScore(mine.score), formatScore(theirs.score), isText = false)
                "visibility" -> FieldDiff(field, "Visibility", Visibility.fromId(mine.visibility).label, Visibility.fromId(theirs.visibility).label, isText = false)
                else -> {
                    val a = reviewText(mine, field).orEmpty()
                    val b = reviewText(theirs, field).orEmpty()
                    val max = if (field == "reviewText") Validation.MAX_REVIEW_LENGTH else Validation.MAX_PROS_CONS_LENGTH
                    FieldDiff(field, REVIEW_LABELS.getValue(field), a, b, isText = true, combined = combine(a, b, max), maxLength = max)
                }
            }
        }

    fun collectionDiffs(mine: CollectionFieldsDto, theirs: CollectionFieldsDto, conflictFields: List<String>): List<FieldDiff> =
        COLLECTION_FIELDS.filter { it in conflictFields }.map { field ->
            when (field) {
                "name" -> FieldDiff(field, "Name", mine.name, theirs.name, isText = true, combined = mine.name, maxLength = Validation.MAX_COLLECTION_NAME_LENGTH)
                else -> FieldDiff(field, "Visibility", Visibility.fromId(mine.visibility).label, Visibility.fromId(theirs.visibility).label, isText = false)
            }
        }

    /**
     * The fields to send after the user decided: a conflicting field as chosen (mine when not decided), a field only
     * I changed (against [base], the version my change started from) mine, any other field theirs: the backend's
     * automatic merge, with the user's choices where it couldn't decide.
     */
    fun resolveReview(base: ReviewFieldsDto?, mine: ReviewFieldsDto, theirs: ReviewFieldsDto, choices: Map<String, FieldChoice>): ReviewFieldsDto {
        fun text(field: String): String? = pick(field, base?.let { reviewText(it, field) }, reviewText(mine, field), reviewText(theirs, field), choices, base != null)
        return ReviewFieldsDto(
            score = pickValue("score", base?.score, mine.score, theirs.score, choices, base != null),
            reviewText = text("reviewText"),
            pros = text("pros"),
            cons = text("cons"),
            visibility = pickValue("visibility", base?.visibility, mine.visibility, theirs.visibility, choices, base != null),
        )
    }

    fun resolveCollection(base: CollectionFieldsDto?, mine: CollectionFieldsDto, theirs: CollectionFieldsDto, choices: Map<String, FieldChoice>): CollectionFieldsDto =
        CollectionFieldsDto(
            name = pick("name", base?.name, mine.name, theirs.name, choices, base != null)?.trim().orEmpty(),
            visibility = pickValue("visibility", base?.visibility, mine.visibility, theirs.visibility, choices, base != null),
        )

    private fun <T> pickValue(field: String, base: T?, mine: T, theirs: T, choices: Map<String, FieldChoice>, hasBase: Boolean): T =
        when (choices[field]) {
            FieldChoice.Theirs -> theirs
            FieldChoice.Mine -> mine
            else -> if (!hasBase || mine != base) mine else theirs
        }

    private fun pick(field: String, base: String?, mine: String?, theirs: String?, choices: Map<String, FieldChoice>, hasBase: Boolean): String? =
        when (val choice = choices[field]) {
            FieldChoice.Theirs -> theirs
            FieldChoice.Mine -> mine
            is FieldChoice.Edited -> choice.text.trim().ifEmpty { null }
            null -> if (!hasBase || !sameText(mine, base)) mine else theirs
        }

    /** What an edit starts from: both texts, mine first (cut to the field's limit). */
    fun combine(mine: String, theirs: String, maxLength: Int): String = when {
        mine.isBlank() -> theirs
        theirs.isBlank() || mine.trim() == theirs.trim() -> mine
        else -> "${mine.trim()}\n\n${theirs.trim()}"
    }.take(maxLength)

    /** Null and "" are the same text, like on the backend. */
    fun sameText(a: String?, b: String?): Boolean = a.orEmpty() == b.orEmpty()

    // Retargeting (a collection created here merged into an existing one)

    /** An op's payload pointing at [realId] instead of the create with [clientRef]. */
    fun retarget(payloadJson: String, clientRef: String, realId: Int): String {
        val payload = OpJson.parseToJsonElement(payloadJson).jsonObject
        val retargeted = payload.mapValues { (_, value) ->
            val ref = (value as? JsonObject)?.get("ref")?.jsonPrimitive?.content
            if (ref == clientRef) JsonObject(mapOf("id" to JsonPrimitive(realId))) else value
        }
        return JsonObject(retargeted).toString()
    }

    /** [dependsOn] without [opId] (null when nothing is left). */
    fun withoutDependency(dependsOn: String?, opId: String): String? =
        dependsOn?.split(',')?.filter { it.isNotBlank() && it != opId }?.joinToString(",")?.ifEmpty { null }

    private fun reviewText(fields: ReviewFieldsDto, field: String): String? = when (field) {
        "reviewText" -> fields.reviewText
        "pros" -> fields.pros
        else -> fields.cons
    }

    private val REVIEW_FIELDS = listOf("score", "reviewText", "pros", "cons", "visibility")
    private val COLLECTION_FIELDS = listOf("name", "visibility")
    private val REVIEW_LABELS = mapOf("reviewText" to "Review", "pros" to "Pros", "cons" to "Cons")
}
