package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.AddMediaRequest
import com.reviewapp.android.data.remote.dto.CollectionMembershipDto
import com.reviewapp.android.data.remote.dto.CollectionWithMediasDto
import com.reviewapp.android.data.remote.dto.LoginResponse
import com.reviewapp.android.data.remote.dto.MediaDto
import com.reviewapp.android.data.remote.dto.MediaReviewDto
import com.reviewapp.android.data.remote.dto.PagedResponse
import com.reviewapp.android.data.remote.dto.ProfileReviewDto
import com.reviewapp.android.data.remote.dto.ReorderMediaRequest
import com.reviewapp.android.data.remote.dto.ReviewCheckResponse
import com.reviewapp.android.data.remote.dto.ReviewDto
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.UpdateCollectionRequest
import com.reviewapp.android.data.remote.dto.UserCompactDto
import com.reviewapp.android.data.remote.dto.UserProfileDto
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import com.reviewapp.android.data.remote.dto.toDto
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.Visibility
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Payloads shaped like the real API responses (camelCase, enums as ints). */
class ApiParsingTest {

    private val json = ApiJson

    @Test
    fun pagedMedia_parsesExternalApiIdCasing() {
        val body = """
            {"items":[{"id":"550Movie","externalApiID":"550","mediaType":0,"title":"Fight Club",
              "releaseDate":"1999-10-15","posterUrl":"https://image.tmdb.org/t/p/w500/x.jpg",
              "overview":"An insomniac...","creator":""}],
             "totalCount":1,"currentPage":1,"totalPages":1}
        """.trimIndent()

        val page = json.decodeFromString<PagedResponse<MediaDto>>(body)
        val media = page.items.single().toDomain()

        assertEquals("550", media.externalApiId)
        assertEquals(MediaType.Movie, media.mediaType)
        assertEquals("0:550", media.key)
        assertEquals(1, page.totalPages)
    }

    @Test
    fun loginResponse_readsBothShapes() {
        val loggedIn = json.decodeFromString<LoginResponse>(
            """{"message":"Logged in successfully","user":{"id":3,"username":"bob","profilePictureUrl":null,"isEmailConfirmed":true}}""",
        )
        assertEquals("bob", loggedIn.user?.username)
        assertFalse(loggedIn.twoFactorRequired)

        val twoFactor = json.decodeFromString<LoginResponse>(
            """{"twoFactorRequired":true,"challengeToken":"abc","message":"We sent a 6-digit code to your email address."}""",
        )
        assertTrue(twoFactor.twoFactorRequired)
        assertEquals("abc", twoFactor.challengeToken)
        assertNull(twoFactor.user)
    }

    @Test
    fun reviewList_parsesDecimalScoreAndDates() {
        val review = json.decodeFromString<MediaReviewDto>(
            """{"id":7,"score":8.5,"reviewText":null,"pros":"Great acting","cons":null,"username":"bob",
               "profilePictureUrl":null,"createdAt":"2026-10-13T10:00:00","updatedAt":"2026-10-13T10:00:00","replyCount":2}""",
        ).toDomain()

        assertEquals(8.5, review.score, 0.0)
        assertNull(review.reviewText)
        assertEquals(2, review.replyCount)
    }

    @Test
    fun reviewCheck_withoutReview() {
        val check = json.decodeFromString<ReviewCheckResponse>("""{"hasReviewed":false,"reviewData":null}""")
        assertFalse(check.hasReviewed)
        assertNull(check.reviewData)
    }

    @Test
    fun reviewRequest_sendsWhatTheApiValidates() {
        val media = MediaDto(externalApiId = "550", mediaType = 0, title = "Fight Club").toDomain()
        val draft = ReviewDraft(score = 7.5, reviewText = "  Loved it  ", pros = "", cons = "Long", visibility = Visibility.FollowersOnly)

        val encoded = json.encodeToString(ReviewMediaRequest.serializer(), ReviewMediaRequest(media.toDto(), draft.toDto()))
        val root = json.parseToJsonElement(encoded).jsonObject
        val mediaDto = root.getValue("mediaDto").jsonObject
        val reviewDto = json.decodeFromJsonElement(ReviewDto.serializer(), root.getValue("reviewDto"))

        // `id` and `externalApiID` are [Required] on the API side
        assertEquals("550Movie", mediaDto.getValue("id").jsonPrimitive.content)
        assertEquals("550", mediaDto.getValue("externalApiID").jsonPrimitive.content)
        assertEquals("Loved it", reviewDto.reviewText)
        assertNull(reviewDto.pros)
        assertEquals(2, reviewDto.visibilityLevel)
    }

    @Test
    fun reviewRequest_sendsValuesEqualToKotlinDefaults() {
        // Public (1) is the Kotlin default of visibilityLevel but the API's default is Private (0),
        // so it must be in the JSON; the same goes for mediaType Movie (0)
        val media = MediaDto(externalApiId = "550", mediaType = 0, title = "Fight Club").toDomain()
        val draft = ReviewDraft(score = 8.0, reviewText = "", pros = "", cons = "", visibility = Visibility.Public)

        val root = json.parseToJsonElement(
            json.encodeToString(ReviewMediaRequest.serializer(), ReviewMediaRequest(media.toDto(), draft.toDto())),
        ).jsonObject

        assertEquals("1", root.getValue("reviewDto").jsonObject.getValue("visibilityLevel").jsonPrimitive.content)
        assertEquals("0", root.getValue("mediaDto").jsonObject.getValue("mediaType").jsonPrimitive.content)
    }

    @Test
    fun profile_parsesCountsAndFollowState() {
        val profile = json.decodeFromString<UserProfileDto>(
            """{"id":3,"username":"bob","bio":null,"profilePictureUrl":"","createdAt":"2026-01-02",
               "followersCount":4,"followingCount":1,"isFollowedByCurrentUser":true}""",
        ).toDomain()

        assertEquals("", profile.bio)
        assertNull(profile.profilePictureUrl)
        assertEquals(4, profile.followersCount)
        assertTrue(profile.isFollowed)
    }

    @Test
    fun userSearch_nullFollowFlagMeansNotFollowed() {
        val users = json.decodeFromString<List<UserCompactDto>>(
            """[{"id":1,"username":"alice","profilePictureUrl":null,"isFollowedByCurrentUser":null}]""",
        ).map { it.toDomain() }

        assertEquals("alice", users.single().username)
        assertFalse(users.single().isFollowed)
    }

    @Test
    fun userUpdate_leavesOutNullFields() {
        val encoded = json.encodeToString(UserUpdateRequest.serializer(), UserUpdateRequest(bio = ""))
        // A missing field means "unchanged" on the API side, an empty bio clears it
        assertEquals("""{"bio":""}""", encoded)
    }

    @Test
    fun profileReview_parsesVisibilityAndOwner() {
        val review = json.decodeFromString<ProfileReviewDto>(
            """{"id":9,"title":"Fight Club","posterUrl":null,"mediaType":0,"externalApiID":"550","score":7.5,
               "reviewText":"Good","pros":null,"cons":null,"createdAt":"2026-10-01","updatedAt":"2026-10-02",
               "visibilityLevel":2,"isOwner":true,"replyCount":0}""",
        ).toDomain()

        assertEquals(Visibility.FollowersOnly, review.visibility)
        assertTrue(review.isOwner)
        assertEquals("0:550", review.mediaKey)
    }

    @Test
    fun collectionWithMedia_parsesDbMediaIdAndOrder() {
        val details = json.decodeFromString<CollectionWithMediasDto>(
            """{"collection":{"id":5,"name":"Favourites","visibilityLevel":0,"createdAt":"2026-01-02","mediaCount":2,
                 "isOwner":true,"isDefault":true},
               "mediaItems":[
                 {"dbMediaID":12,"media":{"id":"550Movie","externalApiID":"550","mediaType":0,"title":"Fight Club"},
                  "orderIndex":0,"addedAt":"2026-10-01T10:00:00.123"},
                 {"dbMediaID":3,"media":{"id":"abcMusic","externalApiID":"abc","mediaType":2,"title":"Song","creator":"Band"},
                  "orderIndex":1,"addedAt":"2026-10-02T10:00:00"}]}""",
        ).toDomain()

        assertTrue(details.collection.isDefault)
        assertEquals(Visibility.Private, details.collection.visibility)
        assertEquals(listOf(12, 3), details.items.map { it.dbMediaId })
        assertEquals("2:abc", details.items[1].media.key)
    }

    @Test
    fun memberships_useMediaKeys() {
        val memberships = json.decodeFromString<List<CollectionMembershipDto>>(
            """[{"collectionID":5,"mediaType":1,"externalApiID":"1399"}]""",
        )
        assertEquals(5, memberships.single().collectionId)
        assertEquals("1:1399", memberships.single().mediaKey)
    }

    @Test
    fun collectionRequests_useApiCasing() {
        val update = json.parseToJsonElement(
            json.encodeToString(UpdateCollectionRequest.serializer(), UpdateCollectionRequest(5, "Watch later", 0)),
        ).jsonObject
        assertEquals("5", update.getValue("collectionID").jsonPrimitive.content)
        // PUT overwrites both fields, so visibility is always sent, Private (0) included
        assertEquals("0", update.getValue("visibilityLevel").jsonPrimitive.content)

        val add = json.encodeToString(AddMediaRequest.serializer(), AddMediaRequest(0, "550"))
        assertEquals("""{"type":0,"externalApiID":"550"}""", add)

        val reorder = json.encodeToString(ReorderMediaRequest.serializer(), ReorderMediaRequest(12, 3))
        assertEquals("""{"dbMediaID":12,"newOrderIndex":3}""", reorder)
    }

    @Test
    fun errorMessage_prefersErrorField() {
        assertEquals("Invalid email or password.", ApiErrors.message(401, """{"error":"Invalid email or password."}"""))
    }

    @Test
    fun errorMessage_readsValidationProblemDetails() {
        val body = """{"type":"x","title":"One or more validation errors occurred.","status":400,
            "errors":{"ReviewDto.Score":["Score must be between 1 and 10."]},"traceId":"t"}"""
        assertEquals("Score must be between 1 and 10.", ApiErrors.message(400, body))
    }

    @Test
    fun errorMessage_fallsBackToStatusCode() {
        assertEquals("Your session has expired. Please log in again.", ApiErrors.message(401, ""))
        assertEquals("Too many requests. Please try again later.", ApiErrors.message(429, "not json"))
        assertTrue(ApiErrors.message(503, null).startsWith("Something went wrong"))
    }
}
