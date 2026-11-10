package com.reviewapp.android.data.local.offline

import android.content.Context
import coil.intercept.Interceptor
import coil.request.ImageResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.security.MessageDigest

/**
 * Small copies of the posters of the user's own reviews and collections, kept outside Coil's cache (which drops
 * the least recently used images within its size) so the user's library always has its covers offline. In
 * `noBackupFilesDir/offline_images/<userId>/`, deleted with the user's offline copy.
 */
class OfflineImageStore(context: Context, private val client: OkHttpClient) {

    private val root = File(context.applicationContext.noBackupFilesDir, "offline_images")

    private fun dirOf(userId: Int) = File(root, userId.toString())

    /** The file of a poster, named by a hash of its URL. */
    fun fileFor(userId: Int, url: String): File = File(dirOf(userId), sha1(url) + ".img")

    /** Whether the poster is already kept. */
    fun has(userId: Int, url: String): Boolean = fileFor(userId, url).exists()

    /** Downloads the small variant of [url]; true when the file is there afterwards. */
    suspend fun download(userId: Int, url: String): Boolean = withContext(Dispatchers.IO) {
        val target = fileFor(userId, url)
        if (target.exists()) return@withContext true
        try {
            client.newCall(Request.Builder().url(PosterUrls.small(url)).build()).execute().use { response ->
                val body = response.body
                if (!response.isSuccessful || body == null) return@withContext false
                target.parentFile?.mkdirs()
                val partial = File(target.path + ".part")
                partial.outputStream().use { out -> body.byteStream().copyTo(out) }
                partial.renameTo(target)
            }
        } catch (_: Exception) {
            false
        }
    }

    /** The disk space the user's poster copies take. */
    fun sizeBytes(userId: Int): Long = dirOf(userId).listFiles()?.sumOf { it.length() } ?: 0L

    /** Deletes all of the user's poster copies. */
    fun deleteUser(userId: Int) {
        dirOf(userId).deleteRecursively()
    }

    private fun sha1(text: String): String =
        MessageDigest.getInstance("SHA-1").digest(text.toByteArray()).joinToString("") { "%02x".format(it) }
}

/** TMDb serves posters in several widths; the offline copy uses a small one. */
object PosterUrls {
    private val tmdbSize = Regex("(image\\.tmdb\\.org/t/p/)(w\\d+|original)/")

    /** The URL of the small variant (185 px wide) of a TMDb poster; any other URL is returned as it is. */
    fun small(url: String): String = tmdbSize.replace(url) { "${it.groupValues[1]}w185/" }
}

/**
 * Coil interceptor: while offline, a poster with an offline copy loads from the file (online the normal request
 * runs, at full size, through Coil's own disk cache).
 */
class OfflineImageInterceptor(
    private val store: OfflineImageStore,
    private val userId: () -> Int?,
    private val isOffline: () -> Boolean,
) : Interceptor {
    override suspend fun intercept(chain: Interceptor.Chain): ImageResult {
        val url = chain.request.data as? String
        val user = userId()
        if (url != null && user != null && isOffline() && url.startsWith("http")) {
            val file = store.fileFor(user, url)
            if (file.exists()) return chain.proceed(chain.request.newBuilder().data(file).build())
        }
        return chain.proceed(chain.request)
    }
}
