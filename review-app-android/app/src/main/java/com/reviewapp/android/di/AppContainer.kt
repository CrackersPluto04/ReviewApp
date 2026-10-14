package com.reviewapp.android.di

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.preferencesDataStore
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.CreationExtras
import com.reviewapp.android.BuildConfig
import com.reviewapp.android.ReviewApplication
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.CryptoStore
import com.reviewapp.android.data.local.SecureCookieJar
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.remote.ApiJson
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.CollectionApi
import com.reviewapp.android.data.remote.FollowerApi
import com.reviewapp.android.data.remote.MediaApi
import com.reviewapp.android.data.remote.ReviewApi
import com.reviewapp.android.data.remote.ReviewReplyApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.repository.AuthRepository
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.ReplyRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.UserRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit

private val Context.sessionDataStore: DataStore<Preferences> by preferencesDataStore(name = "session")
private val Context.settingsDataStore: DataStore<Preferences> by preferencesDataStore(name = "settings")

/** Manual dependency injection: one instance of everything for the app's lifetime. */
class AppContainer(context: Context) {

    val appScope = CoroutineScope(SupervisorJob())

    private val cookieJar = SecureCookieJar(context.sessionDataStore, CryptoStore())

    val settingsStore = SettingsStore(context.settingsDataStore)

    private val okHttpClient: OkHttpClient = OkHttpClient.Builder()
        .cookieJar(cookieJar)
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        // Any 401 means the session is gone (expired, logout-all, password change elsewhere)
        .addInterceptor { chain ->
            val response = chain.proceed(chain.request())
            if (response.code == 401) sessionManager.onUnauthorized()
            response
        }
        .apply {
            if (BuildConfig.DEBUG) {
                // HEADERS would print the session cookie, so only the request lines are logged
                addInterceptor(HttpLoggingInterceptor().setLevel(HttpLoggingInterceptor.Level.BASIC))
            }
        }
        .build()

    private val retrofit: Retrofit = Retrofit.Builder()
        .baseUrl(BuildConfig.API_BASE_URL)
        .client(okHttpClient)
        .addConverterFactory(ApiJson.asConverterFactory("application/json; charset=utf-8".toMediaType()))
        .build()

    private val authApi: AuthApi = retrofit.create(AuthApi::class.java)
    private val mediaApi: MediaApi = retrofit.create(MediaApi::class.java)
    private val reviewApi: ReviewApi = retrofit.create(ReviewApi::class.java)
    private val userApi: UserApi = retrofit.create(UserApi::class.java)
    private val followerApi: FollowerApi = retrofit.create(FollowerApi::class.java)
    private val collectionApi: CollectionApi = retrofit.create(CollectionApi::class.java)
    private val replyApi: ReviewReplyApi = retrofit.create(ReviewReplyApi::class.java)

    val sessionManager = SessionManager(authApi, cookieJar)
    val authRepository = AuthRepository(authApi, sessionManager)
    val mediaRepository = MediaRepository(mediaApi)
    val reviewRepository = ReviewRepository(reviewApi, appScope)
    val userRepository = UserRepository(userApi, followerApi, sessionManager)
    val collectionRepository = CollectionRepository(collectionApi, userApi, sessionManager, appScope)
    val replyRepository = ReplyRepository(replyApi)
}

/** For ViewModel factories: `initializer { val c = appContainer(); ... }` */
fun CreationExtras.appContainer(): AppContainer =
    (this[ViewModelProvider.AndroidViewModelFactory.APPLICATION_KEY] as ReviewApplication).container
