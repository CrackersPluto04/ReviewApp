package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.MediaApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.toMedia
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.mediaKey
import com.reviewapp.android.util.DiscoverOptions
import kotlinx.coroutines.launch

data class TmdbDiscoverFilters(
    val sortBy: String = DiscoverOptions.DEFAULT_TMDB_SORT,
    val genreId: String? = null,
    val year: Int? = null,
    val minRuntime: Int? = null,
    val maxRuntime: Int? = null,
)

data class MusicDiscoverFilters(
    val genre: String = DiscoverOptions.DEFAULT_MUSIC_GENRE,
    val yearFrom: Int? = null,
    val yearTo: Int? = null,
    val market: String = DiscoverOptions.DEFAULT_MARKET,
) {
    /** The API takes `YYYY` or `YYYY-YYYY`. */
    val yearParam: String?
        get() = when {
            yearFrom != null && yearTo != null -> "$yearFrom-$yearTo"
            else -> (yearFrom ?: yearTo)?.toString()
        }
}

/**
 * Media from TMDb/Spotify through the API. Every media returned by a list is also kept in a small
 * in-memory cache, so the details page can open without fetching it again (the web app passes it
 * along in the navigation state instead).
 */
class MediaRepository(
    private val mediaApi: MediaApi,
    private val local: LocalLibrary,
    private val scope: kotlinx.coroutines.CoroutineScope,
) {

    private val cache = object : LinkedHashMap<String, Media>(64, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Media>?) = size > CACHE_SIZE
    }

    fun cached(mediaType: MediaType, externalApiId: String): Media? =
        synchronized(cache) { cache[mediaKey(mediaType, externalApiId)] }

    /** Also used by other repositories that return media (e.g. camera recognition). */
    fun <T : Iterable<Media>> remember(items: T): T {
        synchronized(cache) { items.forEach { cache[it.key] = it } }
        return items
    }

    suspend fun getMedia(mediaType: MediaType, externalApiId: String): ApiResult<Media> {
        cached(mediaType, externalApiId)?.let { return ApiResult.Success(it) }

        return when (val result = apiCall { mediaApi.getMedia(mediaType.id, externalApiId) }) {
            // Offline without a snapshot of its page: the offline library knows the media of the user's own content
            is ApiResult.Error -> offlineMedia(mediaType, externalApiId, result)
            is ApiResult.Success -> {
                // The API answers 200 with an empty media when TMDb/Spotify doesn't know the id
                val media = result.data.toDomain()
                if (media.externalApiId.isBlank() || media.title.isBlank()) {
                    ApiResult.Error("Media not found.", 404)
                } else {
                    remember(listOf(media))
                    // Opened media can be found by the offline search
                    if (result.cachedAt == null) scope.launch { local.rememberMedia(media) }
                    ApiResult.Success(media, result.cachedAt)
                }
            }
        }
    }

    /** Loads a media's page from the API even when it is in memory, so its offline snapshot is current. */
    suspend fun refreshForOffline(mediaType: MediaType, externalApiId: String): ApiResult<Unit> =
        apiCall { mediaApi.getMedia(mediaType.id, externalApiId) }.map { remember(listOf(it.toDomain())); Unit }

    private suspend fun offlineMedia(mediaType: MediaType, externalApiId: String, error: ApiResult.Error): ApiResult<Media> {
        if (!error.isNetwork) return error
        val media = try {
            local.database()?.media()?.get(mediaType.id, externalApiId)?.toMedia()
        } catch (_: Exception) {
            null
        }
        return media?.takeIf { it.title.isNotBlank() }?.let { ApiResult.Success(it, cachedAt = System.currentTimeMillis()) } ?: error
    }

    /**
     * Without a connection both searches look in the offline library (own content and opened media) instead;
     * [ApiResult.Success.cachedAt] set tells the screen so.
     */
    suspend fun searchAll(query: String): ApiResult<List<Media>> =
        when (val result = apiCall { mediaApi.searchAll(query.trim()) }.map { response -> remember(response.items.map { it.toDomain() }) }) {
            is ApiResult.Error -> offlineSearch(query, null, result)
            is ApiResult.Success -> result
        }

    suspend fun search(mediaType: MediaType, query: String, page: Int): ApiResult<Page<Media>> =
        when (val result = apiCall { mediaApi.search(mediaType.pathSegment, query.trim(), page) }
            .map { response -> response.toPage { it.toDomain() }.also { remember(it.items) } }) {
            is ApiResult.Error -> if (page > 1) result else offlineSearch(query, mediaType, result).map { Page(it, 1, 1, it.size) }
            is ApiResult.Success -> result
        }

    private suspend fun offlineSearch(query: String, type: MediaType?, error: ApiResult.Error): ApiResult<List<Media>> {
        if (!error.isNetwork) return error
        val reads = local.reads() ?: return error
        return try {
            ApiResult.Success(remember(reads.search(query, type)), cachedAt = reads.lastSyncedAt() ?: System.currentTimeMillis())
        } catch (_: Exception) {
            error
        }
    }

    suspend fun discover(mediaType: MediaType, filters: TmdbDiscoverFilters, page: Int): ApiResult<Page<Media>> =
        apiCall {
            mediaApi.discoverTmdb(
                type = mediaType.pathSegment,
                page = page,
                sortBy = filters.sortBy,
                year = filters.year,
                withGenres = filters.genreId,
                minRuntime = filters.minRuntime,
                maxRuntime = filters.maxRuntime,
            )
        }.map { response -> response.toPage { it.toDomain() }.also { remember(it.items) } }

    suspend fun discoverMusic(filters: MusicDiscoverFilters, page: Int): ApiResult<Page<Media>> =
        apiCall { mediaApi.discoverMusic(page, filters.genre, filters.yearParam, filters.market) }
            .map { response -> response.toPage { it.toDomain() }.also { remember(it.items) } }

    private companion object {
        const val CACHE_SIZE = 300
    }
}
