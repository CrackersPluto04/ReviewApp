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
import com.reviewapp.android.data.remote.AchievementApi
import com.reviewapp.android.data.remote.ApiJson
import com.reviewapp.android.data.remote.AuthApi
import com.reviewapp.android.data.remote.CollectionApi
import com.reviewapp.android.data.remote.FollowerApi
import com.reviewapp.android.data.remote.MediaApi
import com.reviewapp.android.data.remote.NotificationApi
import com.reviewapp.android.data.remote.RecognitionApi
import com.reviewapp.android.data.local.RecognitionPhotos
import com.reviewapp.android.data.repository.RecognitionRepository
import com.reviewapp.android.data.remote.ReviewApi
import com.reviewapp.android.data.remote.ReviewReplyApi
import com.reviewapp.android.data.remote.SecurityApi
import com.reviewapp.android.data.remote.SubscriptionApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.repository.AccountRepository
import com.reviewapp.android.data.repository.AchievementRepository
import com.reviewapp.android.data.repository.AuthRepository
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.NotificationRepository
import com.reviewapp.android.data.repository.ReplyRepository
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.SubscriptionRepository
import com.reviewapp.android.data.repository.UserRepository
import android.os.SystemClock
import android.provider.Settings
import com.reviewapp.android.data.local.SessionCache
import com.reviewapp.android.data.local.offline.DatabaseKeyStore
import com.reviewapp.android.data.local.offline.DatabaseManager
import com.reviewapp.android.data.sync.ClockReading
import com.reviewapp.android.data.sync.Connectivity
import com.reviewapp.android.data.sync.ConnectivityMonitor
import com.reviewapp.android.data.sync.DeviceClock
import com.reviewapp.android.data.sync.OfflineGateway
import com.reviewapp.android.data.local.CacheBudget
import com.reviewapp.android.data.local.offline.OfflineImageStore
import com.reviewapp.android.data.remote.ProbingCallFactory
import com.reviewapp.android.data.remote.SyncApi
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.SnapshotInterceptor
import com.reviewapp.android.data.sync.SyncEngine
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import java.net.URI
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit

private val Context.sessionDataStore: DataStore<Preferences> by preferencesDataStore(name = "session")
private val Context.settingsDataStore: DataStore<Preferences> by preferencesDataStore(name = "settings")

/** Manual dependency injection: one instance of everything for the app's lifetime. */
class AppContainer(context: Context) {

    val appScope = CoroutineScope(SupervisorJob())

    private val sessionCrypto = CryptoStore()
    private val cookieJar = SecureCookieJar(context.sessionDataStore, sessionCrypto)

    val settingsStore = SettingsStore(context.settingsDataStore)

    private val clock = DeviceClock {
        ClockReading(
            wallMillis = System.currentTimeMillis(),
            elapsedMillis = SystemClock.elapsedRealtime(),
            bootCount = Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1),
        )
    }

    // Asks the backend whether it answers at all while it seemed unreachable: a plain client (no cookie,
    // no interceptors), any HTTP status counts
    private val probeClient = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    val connectivityMonitor = ConnectivityMonitor(context, appScope) {
        withContext(Dispatchers.IO) {
            runCatching {
                probeClient.newCall(Request.Builder().url(BuildConfig.API_BASE_URL + "Subscription/plan").build())
                    .execute().close()
            }.isSuccess
        }
    }

    private val cacheBudgetBytes = settingsStore.cacheBudget.map { it.snapshotBytes }
        .stateIn(appScope, SharingStarted.Eagerly, CacheBudget.Default.snapshotBytes)

    /** The posters of the user's own content, for offline (a plain client: public CDN images, no cookie). */
    val offlineImages = OfflineImageStore(
        context,
        OkHttpClient.Builder().connectTimeout(15, TimeUnit.SECONDS).readTimeout(30, TimeUnit.SECONDS).build(),
    )

    // Offline mode: GET answers are kept and served again when there is no answer (only while the user's offline
    // copy is open; the gateway is created below, the lambda reads it per request)
    val snapshotInterceptor = SnapshotInterceptor(
        database = { offlineGateway.database() },
        budgetBytes = { cacheBudgetBytes.value },
        apiBasePath = URI(BuildConfig.API_BASE_URL).path,
    )

    private val okHttpClient: OkHttpClient = OkHttpClient.Builder()
        .cookieJar(cookieJar)
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        // Outermost, so the connectivity interceptor below still sees a request that got no answer
        .addInterceptor(snapshotInterceptor)
        // Whether the backend answered at all feeds the connectivity state
        .addInterceptor { chain ->
            try {
                chain.proceed(chain.request()).also { connectivityMonitor.onServerResponded() }
            } catch (e: IOException) {
                // A request the app cancelled (screen closed) says nothing about the connection
                if (!chain.call().isCanceled()) connectivityMonitor.onServerUnreachable()
                throw e
            }
        }
        // Any 401 means the session is gone (expired, logout-all, password change elsewhere)
        .addInterceptor { chain ->
            val response = chain.proceed(chain.request())
            if (response.code == 401) sessionManager.onUnauthorized()
            // A Premium feature (or the subscription just ended): the root shows the upsell
            if (response.code == 402) subscriptionRepository.onPremiumRequired()
            response
        }
        // Unlocked achievements come with the response that earned them, and the user's own changes
        // can notify them, so the badge is refreshed shortly after any successful write (like apiFetch)
        .addInterceptor { chain ->
            val request = chain.request()
            val response = chain.proceed(request)
            response.header(AchievementRepository.UNLOCK_HEADER)?.let(achievementRepository::onUnlockHeader)
            if (response.isSuccessful && request.method != "GET") notificationRepository.onMutation()
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
        // Tags each request with its apiCall's probe (whether a snapshot may answer it)
        .callFactory(ProbingCallFactory(okHttpClient))
        .addConverterFactory(ApiJson.asConverterFactory("application/json; charset=utf-8".toMediaType()))
        .build()

    private val authApi: AuthApi = retrofit.create(AuthApi::class.java)
    private val mediaApi: MediaApi = retrofit.create(MediaApi::class.java)
    private val reviewApi: ReviewApi = retrofit.create(ReviewApi::class.java)
    private val userApi: UserApi = retrofit.create(UserApi::class.java)
    private val followerApi: FollowerApi = retrofit.create(FollowerApi::class.java)
    private val collectionApi: CollectionApi = retrofit.create(CollectionApi::class.java)
    private val replyApi: ReviewReplyApi = retrofit.create(ReviewReplyApi::class.java)
    private val notificationApi: NotificationApi = retrofit.create(NotificationApi::class.java)
    private val achievementApi: AchievementApi = retrofit.create(AchievementApi::class.java)
    private val securityApi: SecurityApi = retrofit.create(SecurityApi::class.java)
    private val subscriptionApi: SubscriptionApi = retrofit.create(SubscriptionApi::class.java)

    // A recognition uploads a photo and waits for the AI and the media lookups: longer timeouts than the other
    // calls, with the same cookie jar and interceptors (401 / 402 / unlock / notification handling)
    private val recognitionApi: RecognitionApi = retrofit.newBuilder()
        .callFactory(
            ProbingCallFactory(
                okHttpClient.newBuilder()
                    .writeTimeout(60, TimeUnit.SECONDS)
                    .readTimeout(90, TimeUnit.SECONDS)
                    .build(),
            ),
        )
        .build()
        .create(RecognitionApi::class.java)
    private val syncApi: SyncApi = retrofit.create(SyncApi::class.java)

    val sessionManager = SessionManager(
        authApi = authApi,
        cookieJar = cookieJar,
        cache = SessionCache(context.sessionDataStore, sessionCrypto),
        clock = clock,
        scope = appScope,
        isOnline = { connectivityMonitor.connectivity.value == Connectivity.Online },
        offlineEnabled = { userId -> settingsStore.offlineEnabled(userId).first() },
    )
    val databaseManager = DatabaseManager(context, DatabaseKeyStore(context.sessionDataStore), offlineImages)
    val offlineGateway = OfflineGateway(sessionManager, settingsStore, databaseManager, clock, appScope)
    val localLibrary = LocalLibrary(offlineGateway, sessionManager)
    val authRepository = AuthRepository(authApi, sessionManager)
    val mediaRepository = MediaRepository(mediaApi, localLibrary, appScope)
    val reviewRepository = ReviewRepository(reviewApi, appScope, localLibrary)
    val userRepository = UserRepository(userApi, followerApi, sessionManager, localLibrary)
    val collectionRepository = CollectionRepository(collectionApi, userApi, sessionManager, appScope, localLibrary)
    val replyRepository = ReplyRepository(replyApi)
    val notificationRepository = NotificationRepository(notificationApi, sessionManager, appScope, localLibrary)
    val syncEngine = SyncEngine(syncApi, offlineGateway, connectivityMonitor, snapshotInterceptor, appScope)
    val achievementRepository = AchievementRepository(achievementApi)
    val accountRepository = AccountRepository(userApi, securityApi, authApi, sessionManager)
    val subscriptionRepository = SubscriptionRepository(subscriptionApi, sessionManager, appScope)
    val recognitionRepository = RecognitionRepository(recognitionApi, mediaRepository)
    val recognitionPhotos = RecognitionPhotos(context)
}

/** For ViewModel factories: `initializer { val c = appContainer(); ... }` */
fun CreationExtras.appContainer(): AppContainer =
    (this[ViewModelProvider.AndroidViewModelFactory.APPLICATION_KEY] as ReviewApplication).container
