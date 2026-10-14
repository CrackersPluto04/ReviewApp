package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.AchievementDto
import com.reviewapp.android.data.remote.dto.AddMediaRequest
import com.reviewapp.android.data.remote.dto.ChangeEmailRequest
import com.reviewapp.android.data.remote.dto.ChangePasswordRequest
import com.reviewapp.android.data.remote.dto.CollectionMembershipDto
import com.reviewapp.android.data.remote.dto.CollectionWithMediasDto
import com.reviewapp.android.data.remote.dto.CreateReplyRequest
import com.reviewapp.android.data.remote.dto.CurrentPasswordRequest
import com.reviewapp.android.data.remote.dto.LoginResponse
import com.reviewapp.android.data.remote.dto.MediaDto
import com.reviewapp.android.data.remote.dto.MediaReviewDto
import com.reviewapp.android.data.remote.dto.NotificationPageDto
import com.reviewapp.android.data.remote.dto.PagedResponse
import com.reviewapp.android.data.remote.dto.ProfileReviewDto
import com.reviewapp.android.data.remote.dto.ReorderMediaRequest
import com.reviewapp.android.data.remote.dto.ReplyContextDto
import com.reviewapp.android.data.remote.dto.ReplyPageDto
import com.reviewapp.android.data.remote.dto.ReviewCheckResponse
import com.reviewapp.android.data.remote.dto.ReviewDetailDto
import com.reviewapp.android.data.remote.dto.ReviewDto
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.SecurityStatusDto
import com.reviewapp.android.data.remote.dto.SetSeenRequest
import com.reviewapp.android.data.remote.dto.TwoFactorCodeRequest
import com.reviewapp.android.data.remote.dto.UnseenCountDto
import com.reviewapp.android.data.remote.dto.UpdateCollectionRequest
import com.reviewapp.android.data.remote.dto.UserCompactDto
import com.reviewapp.android.data.remote.dto.UserProfileDto
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import com.reviewapp.android.data.remote.dto.toDto
import com.reviewapp.android.data.repository.AchievementRepository
import com.reviewapp.android.domain.model.AchievementCategory
import com.reviewapp.android.domain.model.AchievementTier
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.NotificationType
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.UnlockedAchievement
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
    fun replyPage_parsesDeletedPlaceholder() {
        val page = json.decodeFromString<ReplyPageDto>(
            """{"items":[
                 {"id":4,"parentReplyID":null,"content":null,"username":null,"profilePictureUrl":null,
                  "createdAt":"2026-10-13","isDeleted":true,"isOwner":false,"childCount":1},
                 {"id":6,"parentReplyID":null,"content":"Nice","username":"bob","profilePictureUrl":null,
                  "createdAt":"2026-10-14","isDeleted":false,"isOwner":true,"childCount":0}],
               "hasMore":true}""",
        ).toDomain()

        assertTrue(page.hasMore)
        val deleted = page.items.first()
        assertTrue(deleted.isDeleted)
        assertNull(deleted.username)
        assertEquals("", deleted.content)
        assertTrue(page.items[1].isTopLevel)
        assertEquals("bob", page.items[1].username)
    }

    @Test
    fun replyContext_withAndWithoutParent() {
        val layer3 = json.decodeFromString<ReplyContextDto>(
            """{"reviewID":7,"reply":{"id":9,"parentReplyID":3,"content":"Agreed","username":"cid","createdAt":"2026-10-14",
                 "isDeleted":false,"isOwner":false,"childCount":0},
               "parentReply":{"id":3,"parentReplyID":null,"content":"Great take","username":"bob","createdAt":"2026-10-13",
                 "isDeleted":false,"isOwner":false,"childCount":1}}""",
        ).toDomain()
        assertEquals(7, layer3.reviewId)
        assertEquals(3, layer3.reply.parentReplyId)
        assertEquals(3, layer3.parentReply?.id)

        val layer2 = json.decodeFromString<ReplyContextDto>(
            """{"reviewID":7,"reply":{"id":3,"parentReplyID":null,"content":"Hi","username":"bob","createdAt":"2026-10-13",
                 "isDeleted":false,"isOwner":false,"childCount":0},"parentReply":null}""",
        ).toDomain()
        assertNull(layer2.parentReply)
    }

    @Test
    fun reviewDetail_parsesAuthorAndVisibility() {
        val detail = json.decodeFromString<ReviewDetailDto>(
            """{"id":7,"username":"asd","profilePictureUrl":null,"title":"Fight Club","posterUrl":null,"mediaType":0,
               "externalApiID":"550","score":8.5,"reviewText":"Good","pros":null,"cons":null,"createdAt":"2026-10-13",
               "updatedAt":"2026-10-13","visibilityLevel":0,"isOwner":true,"replyCount":3}""",
        ).toDomain()

        assertEquals("asd", detail.username)
        assertEquals(Visibility.Private, detail.review.visibility)
        assertEquals(3, detail.review.replyCount)
        assertEquals("0:550", detail.review.mediaKey)
    }

    @Test
    fun createReply_usesApiCasingAndLeavesOutTopLevelParent() {
        assertEquals(
            """{"reviewID":7,"content":"Hi"}""",
            json.encodeToString(CreateReplyRequest.serializer(), CreateReplyRequest(7, null, "Hi")),
        )
        assertEquals(
            """{"reviewID":7,"parentReplyID":3,"content":"Hi"}""",
            json.encodeToString(CreateReplyRequest.serializer(), CreateReplyRequest(7, 3, "Hi")),
        )
    }

    @Test
    fun notificationPage_parsesEveryTypeAndApiCasing() {
        val page = json.decodeFromString<NotificationPageDto>(
            """{"items":[
                {"id":9,"type":"ReplyReply","title":"New reply to your reply","message":"bob replied:\n\"hi\"",
                 "isSeen":false,"createdAt":"2026-10-14T12:34:56.1234567Z","actorUsername":"bob",
                 "actorProfilePictureUrl":"","reviewID":12,"replyID":34,"groupCode":null},
                {"id":8,"type":"AchievementUnlocked","title":"Achievement unlocked","message":"You earned...",
                 "isSeen":true,"createdAt":"2026-10-13T08:00:00Z","actorUsername":null,
                 "actorProfilePictureUrl":null,"reviewID":null,"replyID":null,"groupCode":"CRITIC"},
                {"id":7,"type":"SomethingNew","title":"?","message":"","isSeen":true,"createdAt":"2026-10-12T08:00:00Z"}
               ],"hasMore":true,"totalCount":41,"unseenCount":5}""",
        ).toDomain()

        assertEquals(listOf(9, 8, 7), page.items.map { it.id })
        assertTrue(page.hasMore)
        assertEquals(41, page.totalCount)
        assertEquals(5, page.unseenCount)

        val reply = page.items[0]
        assertEquals(NotificationType.ReplyReply, reply.type)
        assertEquals(12, reply.reviewId)
        assertEquals(34, reply.replyId)
        assertEquals("bob", reply.actorUsername)
        // A blank picture is treated as none
        assertNull(reply.actorProfilePictureUrl)
        assertEquals("bob replied:\n\"hi\"", reply.message)

        val unlock = page.items[1]
        assertEquals(NotificationType.AchievementUnlocked, unlock.type)
        assertEquals("CRITIC", unlock.groupCode)
        assertNull(unlock.actorUsername)
        // Unknown types are shown as system messages
        assertEquals(NotificationType.System, page.items[2].type)
    }

    @Test
    fun notificationRequests_matchApi() {
        assertEquals(7, json.decodeFromString<UnseenCountDto>("""{"count":7}""").count)
        assertEquals("""{"isSeen":false}""", json.encodeToString(SetSeenRequest.serializer(), SetSeenRequest(false)))
    }

    @Test
    fun achievements_parseStringTiersAndCategories() {
        val achievement = json.decodeFromString<List<AchievementDto>>(
            """[{"groupCode":"MOVIE_MASTER","title":"Movie Master","iconKey":"movie-master",
                 "categories":["Movie","Review","Future"],"currentProgress":12,
                 "tiers":[{"tier":"Bronze","description":"Review 1 movie.","targetValue":1,"isUnlocked":true},
                          {"tier":"Silver","description":"Review 10 movies.","targetValue":10,"isUnlocked":true},
                          {"tier":"Gold","description":"Review 50 movies.","targetValue":50,"isUnlocked":false}]}]""",
        ).single().toDomain()

        assertEquals("movie-master", achievement.iconKey)
        // Unknown categories are dropped
        assertEquals(setOf(AchievementCategory.Movie, AchievementCategory.Review), achievement.categories)
        assertEquals(listOf(AchievementTier.Bronze, AchievementTier.Silver, AchievementTier.Gold), achievement.tiers.map { it.tier })
        assertEquals(2, achievement.unlockedCount)
        assertEquals(50, achievement.nextTier?.targetValue)
    }

    @Test
    fun unlockHeader_parsesArrayAndRejectsGarbage() {
        val unlocked = AchievementRepository.parseUnlockHeader(
            """[{"groupCode":"CRITIC","title":"Critic","tier":"Bronze"},{"groupCode":"CURATOR","title":"Curátor","tier":"Gold"}]""",
        )!!
        assertEquals(
            listOf(
                UnlockedAchievement("CRITIC", "Critic", AchievementTier.Bronze),
                UnlockedAchievement("CURATOR", "Curátor", AchievementTier.Gold),
            ),
            unlocked,
        )
        assertNull(AchievementRepository.parseUnlockHeader("not json"))
    }

    @Test
    fun securityStatus_parsesPendingEmail() {
        val none = json.decodeFromString<SecurityStatusDto>(
            """{"email":"a@b.com","isEmailConfirmed":false,"isTwoFactorEnabled":false,"pendingEmail":null}""",
        ).toDomain()
        assertEquals("a@b.com", none.email)
        assertNull(none.pendingEmail)

        val pending = json.decodeFromString<SecurityStatusDto>(
            """{"email":"a@b.com","isEmailConfirmed":true,"isTwoFactorEnabled":true,"pendingEmail":"new@b.com"}""",
        ).toDomain()
        assertTrue(pending.isEmailConfirmed)
        assertTrue(pending.isTwoFactorEnabled)
        assertEquals("new@b.com", pending.pendingEmail)
    }

    @Test
    fun accountRequests_useApiNames() {
        assertEquals(
            """{"newEmail":"n@b.com","currentPassword":"pw"}""",
            json.encodeToString(ChangeEmailRequest.serializer(), ChangeEmailRequest("n@b.com", "pw")),
        )
        assertEquals(
            """{"currentPassword":"old","newPassword":"new"}""",
            json.encodeToString(ChangePasswordRequest.serializer(), ChangePasswordRequest("old", "new")),
        )
        assertEquals("""{"currentPassword":"pw"}""", json.encodeToString(CurrentPasswordRequest.serializer(), CurrentPasswordRequest("pw")))
        assertEquals("""{"code":"012345"}""", json.encodeToString(TwoFactorCodeRequest.serializer(), TwoFactorCodeRequest("012345")))
        // Only the username is sent, the other profile fields stay as they are
        assertEquals("""{"username":"bob"}""", json.encodeToString(UserUpdateRequest.serializer(), UserUpdateRequest(username = "bob")))
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
