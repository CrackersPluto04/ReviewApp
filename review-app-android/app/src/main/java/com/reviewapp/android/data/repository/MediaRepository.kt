package com.reviewapp.android.data.repository

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.MediaApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.toPage
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.mediaKey
import com.reviewapp.android.util.DiscoverOptions

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
class MediaRepository(private val mediaApi: MediaApi) {

    private val cache = object : LinkedHashMap<String, Media>(64, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Media>?) = size > CACHE_SIZE
    }

    fun cached(mediaType: MediaType, externalApiId: String): Media? =
        synchronized(cache) { cache[mediaKey(mediaType, externalApiId)] }

    private fun <T : Iterable<Media>> remember(items: T): T {
        synchronized(cache) { items.forEach { cache[it.key] = it } }
        return items
    }

    suspend fun getMedia(mediaType: MediaType, externalApiId: String): ApiResult<Media> {
        cached(mediaType, externalApiId)?.let { return ApiResult.Success(it) }

        return when (val result = apiCall { mediaApi.getMedia(mediaType.id, externalApiId) }) {
            is ApiResult.Error -> result
            is ApiResult.Success -> {
                // The API answers 200 with an empty media when TMDb/Spotify doesn't know the id
                val media = result.data.toDomain()
                if (media.externalApiId.isBlank() || media.title.isBlank()) {
                    ApiResult.Error("Media not found.", 404)
                } else {
                    remember(listOf(media))
                    ApiResult.Success(media)
                }
            }
        }
    }

    suspend fun searchAll(query: String): ApiResult<List<Media>> =
        apiCall { mediaApi.searchAll(query.trim()) }
            .map { response -> remember(response.items.map { it.toDomain() }) }

    suspend fun search(mediaType: MediaType, query: String, page: Int): ApiResult<Page<Media>> =
        apiCall { mediaApi.search(mediaType.pathSegment, query.trim(), page) }
            .map { response -> response.toPage { it.toDomain() }.also { remember(it.items) } }

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
