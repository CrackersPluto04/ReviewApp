package com.reviewapp.android.ui.recognition

import com.reviewapp.android.domain.model.Recognition
import com.reviewapp.android.domain.model.RecognitionItems
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.RecognizedMedia
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException

/** Where the user is in the recognition flow. Photos are paths of cache files (see `RecognitionPhotos`). */
sealed interface RecognitionStep {
    /** The start page: allowance, tips and the two ways to get a photo. */
    data object Start : RecognitionStep

    /** The live camera. */
    data object Camera : RecognitionStep

    /** The photo is shown for "Use photo" / "Retake"; [fromCamera] decides where Retake leads. */
    data class Preview(val photoPath: String, val fromCamera: Boolean) : RecognitionStep

    /** The photo is being sent and recognised. */
    data class Recognizing(val photoPath: String, val fromCamera: Boolean) : RecognitionStep

    /** The suggestions. */
    data object Results : RecognitionStep
}

/**
 * The suggestions of one recognition and their "Show more" paging (the web's `RecognitionPage` result state).
 * Pages are appended by media key, so an item is never shown twice even if pages arrive twice.
 */
data class RecognitionResults(
    /** The id of the list stored by the API for paging; `null` when nothing was stored. */
    val recognitionId: String?,
    val outcome: RecognitionOutcome,
    /** What the AI saw in the photo, shown above the list. */
    val summary: String,
    val items: List<RecognizedMedia>,
    val hasMore: Boolean,
    /** The `offset` to send for the next "Show more". */
    val nextOffset: Int,
    /** Some lookups failed (a source was unreachable), so the list may be missing items. */
    val incomplete: Boolean,
    val isLoadingMore: Boolean = false,
    val moreError: String? = null,
    /** The stored list is gone (expired, API restarted): no more pages, the user is offered a new photo. */
    val expired: Boolean = false,
) {
    /** The "Show more" button applies. */
    val canShowMore: Boolean get() = hasMore && recognitionId != null && !expired && !isLoadingMore && moreError == null
    /** The list is complete, so the end note shows instead of the button. */
    val isEnd: Boolean get() = items.isNotEmpty() && !hasMore && !expired && moreError == null

    /** A "Show more" request is running. */
    fun loadingMore() = copy(isLoadingMore = true, moreError = null)

    /** Appends a page of suggestions. */
    fun merged(page: RecognitionItems): RecognitionResults {
        val known = items.mapTo(HashSet()) { it.media.key }
        return copy(
            items = items + page.items.filter { known.add(it.media.key) },
            hasMore = page.hasMore,
            nextOffset = page.nextOffset,
            isLoadingMore = false,
            moreError = null,
        )
    }

    /** "Show more" failed; the list stays and a retry is offered. */
    fun failed(message: String) = copy(isLoadingMore = false, moreError = message)

    /** The API no longer has the list (404): paging ends and a new photo is offered. */
    fun expired() = copy(isLoadingMore = false, hasMore = false, expired = true, moreError = null)

    /** The user pressed retry: clears the error so "Show more" can run again. */
    fun retried() = copy(moreError = null)

    companion object {
        /** The results of a fresh recognition: its first page. */
        fun from(recognition: Recognition) = RecognitionResults(
            recognitionId = recognition.recognitionId,
            outcome = recognition.outcome,
            summary = recognition.summary,
            items = recognition.firstPage.items.distinctBy { it.media.key },
            hasMore = recognition.firstPage.hasMore,
            nextOffset = recognition.firstPage.nextOffset,
            incomplete = recognition.incomplete,
        )
    }
}

/** The photo tips on the start page (the web shows the same four). */
val RECOGNITION_TIPS = listOf(
    "Use good light and avoid reflections",
    "Get close, so the object fills most of the photo",
    "Photograph one object at a time",
    "Make any text (titles, names) readable",
)

/** What to say when a recognition has no list to show. */
fun emptyResultMessage(outcome: RecognitionOutcome): String = when (outcome) {
    RecognitionOutcome.NotMediaRelated -> "This doesn't look related to movies, series or music."
    RecognitionOutcome.PersonNotIdentified -> "People are not identified from their faces. Try a figure, poster, cover or other object instead."
    RecognitionOutcome.Recognised -> "We recognised it, but couldn't find matching movies, series or music."
    RecognitionOutcome.NothingRecognised -> "We couldn't recognise anything in this photo."
}

/** e.g. "14 of 20 left today"; null without counters. */
fun allowanceText(remaining: Int?, limit: Int?): String? =
    if (remaining == null || limit == null) null else "$remaining of $limit left today"

/** The local time the daily allowance resets, e.g. "01:00"; null if the date can't be read. */
fun formatResetTime(isoDate: String?, zone: ZoneId = ZoneId.systemDefault()): String? = try {
    isoDate?.let { Instant.parse(it).atZone(zone).toLocalTime().format(DateTimeFormatter.ofPattern("HH:mm")) }
} catch (_: DateTimeParseException) {
    null
}

/** The API's reason codes that change what the screen does, not only what it says. */
object RecognitionReasons {
    const val DAILY_LIMIT = "daily_limit"
    const val NOT_FOUND = "not_found"
}
