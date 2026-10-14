package com.reviewapp.android.data.repository

import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.CollectionApi
import com.reviewapp.android.data.remote.UserApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.AddMediaRequest
import com.reviewapp.android.data.remote.dto.CreateCollectionRequest
import com.reviewapp.android.data.remote.dto.ReorderMediaRequest
import com.reviewapp.android.data.remote.dto.UpdateCollectionRequest
import com.reviewapp.android.data.remote.map
import com.reviewapp.android.domain.model.CollectionDetails
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.util.DiscoverOptions
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * The logged-in user's collections and which media they contain (by media key), so media cards
 * can show the favourite and collection state without a request per card.
 */
data class MyCollections(
    val collections: List<MediaCollection> = emptyList(),
    val memberships: Map<String, Set<Int>> = emptyMap(),
    val isLoaded: Boolean = false,
    val isLoading: Boolean = false,
    val error: String? = null,
) {
    val favourites: MediaCollection? get() = collections.firstOrNull { it.isDefault }

    fun contains(mediaKey: String, collectionId: Int): Boolean = collectionId in memberships[mediaKey].orEmpty()

    fun isFavourite(mediaKey: String): Boolean = favourites?.let { contains(mediaKey, it.id) } == true

    fun withMembership(mediaKey: String, collectionId: Int, inCollection: Boolean): MyCollections {
        val current = memberships[mediaKey].orEmpty()
        val updated = if (inCollection) current + collectionId else current - collectionId
        return copy(memberships = if (updated.isEmpty()) memberships - mediaKey else memberships + (mediaKey to updated))
    }

    fun withoutCollection(collectionId: Int): MyCollections = copy(
        collections = collections.filterNot { it.id == collectionId },
        memberships = memberships
            .mapValues { (_, ids) -> ids - collectionId }
            .filterValues { it.isNotEmpty() },
    )
}

/** Emitted after the current user changed one of their collections or its media. */
data class CollectionChange(val collectionId: Int)

class CollectionRepository(
    private val collectionApi: CollectionApi,
    private val userApi: UserApi,
    private val sessionManager: SessionManager,
    private val appScope: CoroutineScope,
) {
    private val _mine = MutableStateFlow(MyCollections())
    val mine: StateFlow<MyCollections> = _mine.asStateFlow()

    private val _changes = MutableSharedFlow<CollectionChange>(extraBufferCapacity = 8)
    val changes: SharedFlow<CollectionChange> = _changes.asSharedFlow()

    // Membership changes run one at a time, so quick repeated taps toggle in order
    private val mutex = Mutex()
    private var loadJob: Job? = null

    init {
        appScope.launch {
            sessionManager.state
                .map { (it as? AuthState.LoggedIn)?.user?.id }
                .distinctUntilChanged()
                .collect { userId ->
                    loadJob?.cancel()
                    _mine.value = MyCollections()
                    if (userId != null) refresh()
                }
        }
    }

    /** Reloads the current user's collections and memberships in the background. */
    fun refresh() {
        val username = sessionManager.currentUser?.username ?: return
        loadJob?.cancel()
        _mine.update { it.copy(isLoading = true, error = null) }
        loadJob = appScope.launch {
            val (collections, memberships) = coroutineScope {
                val collections = async { apiCall { userApi.getCollections(username, DiscoverOptions.DEFAULT_COLLECTION_SORT) } }
                val memberships = async { apiCall { collectionApi.getMemberships() } }
                collections.await() to memberships.await()
            }
            _mine.update { current ->
                if (collections is ApiResult.Success && memberships is ApiResult.Success) {
                    MyCollections(
                        collections = collections.data.map { it.toDomain() },
                        memberships = memberships.data.groupBy({ it.mediaKey }, { it.collectionId }).mapValues { it.value.toSet() },
                        isLoaded = true,
                    )
                } else {
                    val error = (collections as? ApiResult.Error ?: memberships as ApiResult.Error).message
                    current.copy(isLoading = false, error = error)
                }
            }
        }
    }

    /** Loads them if an earlier attempt failed (e.g. the add-to-collection sheet opened offline). */
    fun refreshIfNeeded() {
        val state = _mine.value
        if (!state.isLoaded && !state.isLoading) refresh()
    }

    /** Toggles the media in Favourites, returns whether it is a favourite now. */
    suspend fun toggleFavourite(media: Media): ApiResult<Boolean> = mutex.withLock {
        val favourites = _mine.value.favourites
            ?: return@withLock ApiResult.Error("Could not find your Favourites collection.")
        val add = !_mine.value.contains(media.key, favourites.id)
        setMembershipLocked(media, favourites.id, add).map { add }
    }

    suspend fun setInCollection(media: Media, collectionId: Int, add: Boolean): ApiResult<Unit> =
        mutex.withLock { setMembershipLocked(media, collectionId, add) }

    // Optimistic: the cache changes first and is put back (and reloaded) if the request fails
    private suspend fun setMembershipLocked(media: Media, collectionId: Int, add: Boolean): ApiResult<Unit> {
        _mine.update { it.withMembership(media.key, collectionId, add) }
        val result = apiCall {
            if (add) {
                collectionApi.addMedia(collectionId, AddMediaRequest(media.mediaType.id, media.externalApiId))
            } else {
                collectionApi.removeMedia(collectionId, media.mediaType.id, media.externalApiId)
            }
        }
        when (result) {
            is ApiResult.Success -> _changes.tryEmit(CollectionChange(collectionId))
            is ApiResult.Error -> {
                _mine.update { it.withMembership(media.key, collectionId, !add) }
                refresh()
            }
        }
        return result
    }

    suspend fun userCollections(username: String, sortBy: String): ApiResult<List<MediaCollection>> =
        apiCall { userApi.getCollections(username, sortBy) }.map { list -> list.map { it.toDomain() } }

    suspend fun details(id: Int): ApiResult<CollectionDetails> =
        apiCall { collectionApi.getCollection(id) }.map { it.toDomain() }

    suspend fun create(name: String, visibility: Visibility): ApiResult<MediaCollection> {
        val result = apiCall { collectionApi.create(CreateCollectionRequest(name.trim(), visibility.id)) }
            .map { it.toDomain() }
        if (result is ApiResult.Success) {
            _mine.update { it.copy(collections = it.collections + result.data) }
            _changes.tryEmit(CollectionChange(result.data.id))
        }
        return result
    }

    suspend fun update(collection: MediaCollection, name: String, visibility: Visibility): ApiResult<MediaCollection> {
        val result = apiCall {
            collectionApi.update(UpdateCollectionRequest(collection.id, name.trim(), visibility.id))
        }.map { collection.copy(name = it.name, visibility = visibility) }
        if (result is ApiResult.Success) {
            _mine.update { mine ->
                mine.copy(collections = mine.collections.map { if (it.id == collection.id) result.data else it })
            }
            _changes.tryEmit(CollectionChange(collection.id))
        }
        return result
    }

    suspend fun delete(collectionId: Int): ApiResult<Unit> {
        val result = apiCall { collectionApi.delete(collectionId) }
        if (result is ApiResult.Success) {
            _mine.update { it.withoutCollection(collectionId) }
            _changes.tryEmit(CollectionChange(collectionId))
        }
        return result
    }

    /** Moves one media to [newIndex] (0-based, the API shifts the rest). */
    suspend fun reorder(collectionId: Int, dbMediaId: Int, newIndex: Int): ApiResult<Unit> =
        apiCall { collectionApi.reorder(collectionId, ReorderMediaRequest(dbMediaId, newIndex)) }
}
