package com.reviewapp.android.ui.recognition

import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Recognition
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionItems
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.RecognitionTier
import com.reviewapp.android.domain.model.RecognizedMedia
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.ZoneId

class RecognitionResultsTest {

    private fun item(id: String, tier: RecognitionTier = RecognitionTier.Direct, type: MediaType = MediaType.Movie) =
        RecognizedMedia(Media(id, type, "Title $id", null, null, "", ""), tier)

    private fun page(vararg ids: String, hasMore: Boolean = true, nextOffset: Int = 15, total: Int = 20) =
        RecognitionItems(ids.map { item(it) }, total, hasMore, nextOffset)

    private fun recognition(firstPage: RecognitionItems, id: String? = "rec-1") = Recognition(
        recognitionId = id,
        outcome = RecognitionOutcome.Recognised,
        summary = "We saw it.",
        firstPage = firstPage,
        incomplete = false,
        allowance = RecognitionAllowance(true, true, 20, 19, null),
    )

    private fun first(): RecognitionResults =
        RecognitionResults.from(recognition(page(*(1..10).map { "$it" }.toTypedArray(), nextOffset = 10)))

    @Test
    fun from_takesTheFirstPage() {
        val results = first()

        assertEquals(10, results.items.size)
        assertEquals("rec-1", results.recognitionId)
        assertEquals(10, results.nextOffset)
        assertTrue(results.canShowMore)
        assertFalse(results.isEnd)
    }

    @Test
    fun merged_appendsWithoutDuplicatesAndMovesTheOffset() {
        val results = first().loadingMore().merged(page("10", "11", "12", nextOffset = 15))

        assertEquals((1..12).map { "$it" }, results.items.map { it.media.externalApiId })
        assertEquals(15, results.nextOffset)
        assertFalse(results.isLoadingMore)
    }

    @Test
    fun sameIdOfAnotherType_isAnotherItem() {
        val results = first().merged(RecognitionItems(listOf(item("1", type = MediaType.Series)), 11, false, 11))

        assertEquals(11, results.items.size)
    }

    @Test
    fun lastPage_endsTheList() {
        val results = first().merged(page("11", "12", hasMore = false, nextOffset = 12))

        assertFalse(results.canShowMore)
        assertTrue(results.isEnd)
    }

    @Test
    fun loadingOrFailed_blocksShowMore_untilRetried() {
        val loading = first().loadingMore()
        assertFalse(loading.canShowMore)

        val failed = loading.failed("Could not load more.")
        assertEquals("Could not load more.", failed.moreError)
        assertFalse(failed.isLoadingMore)
        assertFalse(failed.canShowMore)
        assertFalse(failed.isEnd)
        // The list stays
        assertEquals(10, failed.items.size)

        assertTrue(failed.retried().canShowMore)
    }

    @Test
    fun expired_stopsPaging() {
        val results = first().loadingMore().expired()

        assertTrue(results.expired)
        assertFalse(results.canShowMore)
        assertFalse(results.isEnd)
        assertEquals(10, results.items.size)
    }

    @Test
    fun shortList_hasNothingToPageThrough() {
        val results = RecognitionResults.from(recognition(page("1", "2", hasMore = false, nextOffset = 2, total = 2), id = null))

        assertFalse(results.canShowMore)
        assertTrue(results.isEnd)
    }

    @Test
    fun emptyList_isNeitherMoreNorEnd() {
        val results = RecognitionResults.from(recognition(RecognitionItems(emptyList(), 0, false, 0), id = null))

        assertFalse(results.canShowMore)
        assertFalse(results.isEnd)
    }

    @Test
    fun texts() {
        assertEquals("14 of 20 left today", allowanceText(14, 20))
        assertNull(allowanceText(null, 20))
        assertEquals("We couldn't recognise anything in this photo.", emptyResultMessage(RecognitionOutcome.NothingRecognised))
        assertTrue(emptyResultMessage(RecognitionOutcome.PersonNotIdentified).startsWith("People are not identified"))
        assertEquals("00:00", formatResetTime("2026-11-03T00:00:00Z", ZoneId.of("UTC")))
        assertEquals("01:00", formatResetTime("2026-11-03T00:00:00Z", ZoneId.of("Europe/Budapest")))
        assertNull(formatResetTime("tomorrow"))
        assertNull(formatResetTime(null))
    }

    @Test
    fun tiers_haveUserFacingNames_andUnknownOnesAreTheLoosest() {
        assertEquals(listOf("Direct match", "Same universe", "Related people", "You might also like"), RecognitionTier.entries.map { it.label })
        assertEquals(RecognitionTier.People, RecognitionTier.fromApi("people"))
        assertEquals(RecognitionTier.Similar, RecognitionTier.fromApi("cousins"))
        assertEquals(RecognitionTier.Similar, RecognitionTier.fromApi(null))
    }
}
