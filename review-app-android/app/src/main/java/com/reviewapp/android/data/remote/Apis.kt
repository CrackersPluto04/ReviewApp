package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.AddMediaRequest
import com.reviewapp.android.data.remote.dto.AverageScoreResponse
import com.reviewapp.android.data.remote.dto.CollectionDto
import com.reviewapp.android.data.remote.dto.CollectionMembershipDto
import com.reviewapp.android.data.remote.dto.CollectionWithMediasDto
import com.reviewapp.android.data.remote.dto.CreateCollectionRequest
import com.reviewapp.android.data.remote.dto.CreateReplyRequest
import com.reviewapp.android.data.remote.dto.FeedReviewDto
import com.reviewapp.android.data.remote.dto.LoginRequest
import com.reviewapp.android.data.remote.dto.LoginResponse
import com.reviewapp.android.data.remote.dto.MediaDto
import com.reviewapp.android.data.remote.dto.MediaReviewDto
import com.reviewapp.android.data.remote.dto.MessageResponse
import com.reviewapp.android.data.remote.dto.PagedResponse
import com.reviewapp.android.data.remote.dto.ProfileReviewDto
import com.reviewapp.android.data.remote.dto.RegisterRequest
import com.reviewapp.android.data.remote.dto.ReorderMediaRequest
import com.reviewapp.android.data.remote.dto.ReplyContextDto
import com.reviewapp.android.data.remote.dto.ReplyDto
import com.reviewapp.android.data.remote.dto.ReplyPageDto
import com.reviewapp.android.data.remote.dto.ReviewCheckResponse
import com.reviewapp.android.data.remote.dto.ReviewDetailDto
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.TwoFactorLoginRequest
import com.reviewapp.android.data.remote.dto.TwoFactorResendRequest
import com.reviewapp.android.data.remote.dto.UpdateCollectionRequest
import com.reviewapp.android.data.remote.dto.UserCompactDto
import com.reviewapp.android.data.remote.dto.UserDto
import com.reviewapp.android.data.remote.dto.UserProfileDto
import com.reviewapp.android.data.remote.dto.UserUpdateRequest
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query

// One interface per API controller, paths are relative to BuildConfig.API_BASE_URL (".../api/").

interface AuthApi {
    @POST("Auth/register")
    suspend fun register(@Body body: RegisterRequest): MessageResponse

    @POST("Auth/login")
    suspend fun login(@Body body: LoginRequest): LoginResponse

    @POST("Auth/login/2fa")
    suspend fun loginTwoFactor(@Body body: TwoFactorLoginRequest): LoginResponse

    @POST("Auth/login/2fa/resend")
    suspend fun resendTwoFactorCode(@Body body: TwoFactorResendRequest): MessageResponse

    @POST("Auth/logout")
    suspend fun logout(): MessageResponse

    @GET("Auth/check-auth")
    suspend fun checkAuth(): UserDto
}

interface MediaApi {
    @GET("Media/media/{mediaType}/{externalApiId}")
    suspend fun getMedia(
        @Path("mediaType") mediaType: Int,
        @Path("externalApiId") externalApiId: String,
    ): MediaDto

    /** Up to 5 movies + 5 series + 5 tracks, always a single page. */
    @GET("Media/search/all")
    suspend fun searchAll(@Query("query") query: String): PagedResponse<MediaDto>

    /** [type] is `movie`, `series` or `music`. */
    @GET("Media/search/{type}")
    suspend fun search(
        @Path("type") type: String,
        @Query("query") query: String,
        @Query("page") page: Int,
    ): PagedResponse<MediaDto>

    /** [type] is `movie` or `series`. */
    @GET("Media/discover/{type}")
    suspend fun discoverTmdb(
        @Path("type") type: String,
        @Query("page") page: Int,
        @Query("sortBy") sortBy: String,
        @Query("year") year: Int?,
        @Query("withGenres") withGenres: String?,
        @Query("minRuntime") minRuntime: Int?,
        @Query("maxRuntime") maxRuntime: Int?,
    ): PagedResponse<MediaDto>

    /** [year] is `YYYY` or `YYYY-YYYY`. */
    @GET("Media/discover/music")
    suspend fun discoverMusic(
        @Query("page") page: Int,
        @Query("genre") genre: String,
        @Query("year") year: String?,
        @Query("market") market: String,
    ): PagedResponse<MediaDto>
}

interface ReviewApi {
    @GET("Review/recent")
    suspend fun getRecent(@Query("count") count: Int): List<FeedReviewDto>

    @GET("Review/media/{mediaType}/{externalApiId}")
    suspend fun getMediaReviews(
        @Path("mediaType") mediaType: Int,
        @Path("externalApiId") externalApiId: String,
        @Query("page") page: Int,
        @Query("sortBy") sortBy: String,
        @Query("minScore") minScore: Double,
        @Query("maxScore") maxScore: Double,
        @Query("hasWrittenText") hasWrittenText: Boolean,
    ): PagedResponse<MediaReviewDto>

    @GET("Review/stats/average-score")
    suspend fun getAverageScore(
        @Query("externalApiId") externalApiId: String,
        @Query("mediaType") mediaType: Int,
    ): AverageScoreResponse

    /** The current user's own review of a media. */
    @GET("Review/check")
    suspend fun checkReview(
        @Query("externalApiId") externalApiId: String,
        @Query("mediaType") mediaType: Int,
    ): ReviewCheckResponse

    @POST("Review")
    suspend fun createReview(@Body body: ReviewMediaRequest): MessageResponse

    /** Finds the review by media + current user, not by id. */
    @PUT("Review")
    suspend fun editReview(@Body body: ReviewMediaRequest): MessageResponse

    /** One review with its author; hidden ones (private, followers only) are a 404. */
    @GET("Review/{id}")
    suspend fun getReview(@Path("id") id: Int): ReviewDetailDto

    @DELETE("Review/{id}")
    suspend fun deleteReview(@Path("id") id: Int)
}

interface UserApi {
    /** Up to 10 users whose name contains [query]. */
    @GET("User/search")
    suspend fun search(@Query("q") query: String): List<UserCompactDto>

    @GET("User/{username}")
    suspend fun getProfile(@Path("username") username: String): UserProfileDto

    @PATCH("User/me")
    suspend fun updateMe(@Body body: UserUpdateRequest): MessageResponse

    /** Only the reviews the caller may see (owner: all, follower: public + followers only, others: public). */
    @GET("User/{username}/reviews")
    suspend fun getReviews(
        @Path("username") username: String,
        @Query("page") page: Int,
        @Query("sortBy") sortBy: String,
        @Query("minScore") minScore: Double,
        @Query("maxScore") maxScore: Double,
        @Query("hasWrittenText") hasWrittenText: Boolean,
    ): PagedResponse<ProfileReviewDto>

    /** Same visibility rules as [getReviews]. */
    @GET("User/{username}/collections")
    suspend fun getCollections(
        @Path("username") username: String,
        @Query("sortBy") sortBy: String,
    ): List<CollectionDto>

    @GET("User/{username}/followers")
    suspend fun getFollowers(@Path("username") username: String): List<UserCompactDto>

    @GET("User/{username}/following")
    suspend fun getFollowing(@Path("username") username: String): List<UserCompactDto>
}

interface FollowerApi {
    @POST("Follower/{userId}/follow")
    suspend fun follow(@Path("userId") userId: Int)

    @DELETE("Follower/{userId}/unfollow")
    suspend fun unfollow(@Path("userId") userId: Int)

    /** Removes someone who follows the current user. */
    @DELETE("Follower/{userId}/remove")
    suspend fun removeFollower(@Path("userId") userId: Int)
}

interface CollectionApi {
    /** Every media in the current user's collections. */
    @GET("Collection/me/memberships")
    suspend fun getMemberships(): List<CollectionMembershipDto>

    @GET("Collection/{id}")
    suspend fun getCollection(@Path("id") id: Int): CollectionWithMediasDto

    @POST("Collection")
    suspend fun create(@Body body: CreateCollectionRequest): CollectionDto

    /** The response's `isDefault` and `mediaCount` aren't reliable, keep the old values. */
    @PUT("Collection")
    suspend fun update(@Body body: UpdateCollectionRequest): CollectionDto

    @DELETE("Collection/{id}")
    suspend fun delete(@Path("id") id: Int)

    @POST("Collection/{id}/media")
    suspend fun addMedia(@Path("id") id: Int, @Body body: AddMediaRequest)

    @DELETE("Collection/{id}/media/{mediaType}-{externalApiId}")
    suspend fun removeMedia(
        @Path("id") id: Int,
        @Path("mediaType") mediaType: Int,
        @Path("externalApiId") externalApiId: String,
    )

    /** Moves one item, the API shifts the others. */
    @PUT("Collection/{id}/reorder")
    suspend fun reorder(@Path("id") id: Int, @Body body: ReorderMediaRequest)
}

interface ReviewReplyApi {
    /** Oldest first; [parentReplyId] null lists the replies to the review, otherwise that reply's children. */
    @GET("ReviewReply")
    suspend fun getReplies(
        @Query("reviewId") reviewId: Int,
        @Query("parentReplyId") parentReplyId: Int?,
        @Query("afterId") afterId: Int?,
        @Query("pageSize") pageSize: Int,
    ): ReplyPageDto

    /** A reply and its parent (if it's a reply to a reply); deleted or hidden ones are a 404. */
    @GET("ReviewReply/{id}")
    suspend fun getReplyContext(@Path("id") id: Int): ReplyContextDto

    @POST("ReviewReply")
    suspend fun create(@Body body: CreateReplyRequest): ReplyDto

    /** Soft delete, only the author's own reply. */
    @DELETE("ReviewReply/{id}")
    suspend fun delete(@Path("id") id: Int)
}
