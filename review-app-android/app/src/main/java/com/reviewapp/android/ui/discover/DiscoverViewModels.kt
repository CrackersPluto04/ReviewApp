package com.reviewapp.android.ui.discover

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.MediaRepository
import com.reviewapp.android.data.repository.MusicDiscoverFilters
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.TmdbDiscoverFilters
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.PagedLoader
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Shared part of both discover pages: the filters and an infinite list that restarts whenever
 * they change (no Apply button, unlike the web app).
 */
abstract class BaseDiscoverViewModel<F>(
    initialFilters: F,
    private val reviewRepository: ReviewRepository,
) : ViewModel() {

    val defaultFilters = initialFilters
    private val _filters = MutableStateFlow(initialFilters)
    val filters: StateFlow<F> = _filters.asStateFlow()

    val scoreStats: StateFlow<Map<String, ScoreStats>> = reviewRepository.scoreStats

    private val loader = PagedLoader<Media>(viewModelScope, itemKey = { it.key }) { page ->
        fetch(_filters.value, page)
    }
    val list: StateFlow<PagedList<Media>> = loader.state

    protected abstract suspend fun fetch(filters: F, page: Int): ApiResult<Page<Media>>

    /** Called from the subclass `init`, after its own properties are set. */
    protected fun start() = loader.reload()

    fun updateFilters(filters: F) {
        if (filters == _filters.value) return
        _filters.value = filters
        loader.reload()
    }

    fun clearFilters() = updateFilters(defaultFilters)

    fun loadMore() = loader.loadMore()
    fun retry() = loader.retry()
    fun requestStats(media: Media) = reviewRepository.requestStats(media.mediaType, media.externalApiId)
}

class TmdbDiscoverViewModel(
    val mediaType: MediaType,
    private val mediaRepository: MediaRepository,
    reviewRepository: ReviewRepository,
) : BaseDiscoverViewModel<TmdbDiscoverFilters>(TmdbDiscoverFilters(), reviewRepository) {

    init {
        start()
    }

    override suspend fun fetch(filters: TmdbDiscoverFilters, page: Int) =
        mediaRepository.discover(mediaType, filters, page)

    companion object {
        fun factory(mediaType: MediaType): ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                TmdbDiscoverViewModel(mediaType, c.mediaRepository, c.reviewRepository)
            }
        }
    }
}

class MusicDiscoverViewModel(
    private val mediaRepository: MediaRepository,
    reviewRepository: ReviewRepository,
) : BaseDiscoverViewModel<MusicDiscoverFilters>(MusicDiscoverFilters(), reviewRepository) {

    init {
        start()
    }

    override suspend fun fetch(filters: MusicDiscoverFilters, page: Int) =
        mediaRepository.discoverMusic(filters, page)

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                MusicDiscoverViewModel(c.mediaRepository, c.reviewRepository)
            }
        }
    }
}
