package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.RecognitionAllowanceDto
import com.reviewapp.android.data.remote.dto.RecognitionItemsDto
import com.reviewapp.android.data.remote.dto.RecognitionResponseDto
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.RecognitionTier
import kotlinx.serialization.decodeFromString
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The camera recognition responses as the API sends them (see `RecognitionController`). */
class RecognitionParsingTest {

    private val json = ApiJson

    @Test
    fun recognition_readsSummaryItemsTiersAndAllowance() {
        val recognition = json.decodeFromString<RecognitionResponseDto>(
            """{"recognitionId":"abc_-1","outcome":"recognised","summary":"We saw an Iron Man statue.",
               "subject":{"label":"Iron Man statue","kind":"statue"},
               "items":[
                 {"media":{"id":"1726Movie","externalApiID":"1726","mediaType":0,"title":"Iron Man","releaseDate":"2008-04-30",
                   "posterUrl":"https://image.tmdb.org/t/p/w500/x.jpg","overview":"","creator":""},"tier":"direct"},
                 {"media":{"id":"sp1Music","externalApiID":"sp1","mediaType":2,"title":"Back in Black","creator":"AC/DC"},"tier":"universe"}
               ],
               "total":12,"hasMore":true,"nextOffset":10,"incomplete":false,
               "allowance":{"enabled":true,"premium":true,"limit":20,"remaining":13,"resetsAt":"2026-11-03T00:00:00Z"}}""",
        ).toDomain()

        assertEquals("abc_-1", recognition.recognitionId)
        assertEquals(RecognitionOutcome.Recognised, recognition.outcome)
        assertEquals("We saw an Iron Man statue.", recognition.summary)
        assertEquals(listOf(RecognitionTier.Direct, RecognitionTier.Universe), recognition.firstPage.items.map { it.tier })
        assertEquals("1726", recognition.firstPage.items[0].media.externalApiId)
        assertEquals(MediaType.Music, recognition.firstPage.items[1].media.mediaType)
        assertEquals("AC/DC", recognition.firstPage.items[1].media.creator)
        assertEquals(12, recognition.firstPage.total)
        assertTrue(recognition.firstPage.hasMore)
        assertEquals(10, recognition.firstPage.nextOffset)
        assertEquals(RecognitionAllowance(true, true, 20, 13, "2026-11-03T00:00:00Z"), recognition.allowance)
    }

    @Test
    fun recognition_withNothingToSuggest() {
        val recognition = json.decodeFromString<RecognitionResponseDto>(
            """{"recognitionId":null,"outcome":"not_media_related","summary":"We saw a mug.","subject":null,"items":[],
               "total":0,"hasMore":false,"nextOffset":0,"incomplete":false,"allowance":{"enabled":true,"premium":true,"limit":20,"remaining":19}}""",
        ).toDomain()

        assertNull(recognition.recognitionId)
        assertEquals(RecognitionOutcome.NotMediaRelated, recognition.outcome)
        assertTrue(recognition.firstPage.items.isEmpty())
        assertNull(recognition.allowance.resetsAt)
    }

    @Test
    fun unknownValues_fallBackSafely() {
        val recognition = json.decodeFromString<RecognitionResponseDto>(
            """{"outcome":"something_new","summary":"x","items":[{"media":{"externalApiID":"1","mediaType":0,"title":"A"},"tier":"cousins"}],
               "newField":42}""",
        ).toDomain()

        assertEquals(RecognitionOutcome.NothingRecognised, recognition.outcome)
        assertEquals(RecognitionTier.Similar, recognition.firstPage.items.single().tier)
    }

    @Test
    fun itemsPage_parses() {
        val page = json.decodeFromString<RecognitionItemsDto>(
            """{"items":[{"media":{"externalApiID":"9","mediaType":1,"title":"Show"},"tier":"people"}],"total":11,"hasMore":false,"nextOffset":11}""",
        ).toDomain()

        assertEquals(RecognitionTier.People, page.items.single().tier)
        assertEquals(MediaType.Series, page.items.single().media.mediaType)
        assertFalse(page.hasMore)
        assertEquals(11, page.nextOffset)
    }

    @Test
    fun allowance_ofAFreeUser_hasNoCounters() {
        val allowance = json.decodeFromString<RecognitionAllowanceDto>(
            """{"enabled":true,"premium":false,"limit":null,"remaining":null,"resetsAt":null}""",
        ).toDomain()

        assertEquals(RecognitionAllowance(enabled = true, premium = false, limit = null, remaining = null, resetsAt = null), allowance)
    }

    @Test
    fun errorReason_isReadFromTheBody() {
        assertEquals("daily_limit", ApiErrors.reason("""{"error":"You have used all 20 recognitions for today.","code":"daily_limit"}"""))
        assertEquals("You have used all 20 recognitions for today.",
            ApiErrors.message(429, """{"error":"You have used all 20 recognitions for today.","code":"daily_limit"}"""))
        assertNull(ApiErrors.reason("""{"error":"Too many requests. Please try again later."}"""))
        assertNull(ApiErrors.reason("not json"))
        assertNull(ApiErrors.reason(null))
    }
}
