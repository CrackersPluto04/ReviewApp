package com.reviewapp.android.data.remote

import com.reviewapp.android.data.remote.dto.AverageScoreResponse
import com.reviewapp.android.data.remote.dto.FeedReviewDto
import com.reviewapp.android.data.remote.dto.LoginRequest
import com.reviewapp.android.data.remote.dto.LoginResponse
import com.reviewapp.android.data.remote.dto.MediaDto
import com.reviewapp.android.data.remote.dto.MediaReviewDto
import com.reviewapp.android.data.remote.dto.MessageResponse
import com.reviewapp.android.data.remote.dto.PagedResponse
import com.reviewapp.android.data.remote.dto.RegisterRequest
import com.reviewapp.android.data.remote.dto.ReviewCheckResponse
import com.reviewapp.android.data.remote.dto.ReviewMediaRequest
import com.reviewapp.android.data.remote.dto.TwoFactorLoginRequest
import com.reviewapp.android.data.remote.dto.TwoFactorResendRequest
import com.reviewapp.android.data.remote.dto.UserDto
import retrofit2.http.Body
import retrofit2.http.GET
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
}
