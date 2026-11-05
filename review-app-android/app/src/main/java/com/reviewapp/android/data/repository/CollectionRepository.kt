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
import com.reviewapp.android.data.sync.LocalLibrary
import com.reviewapp.android.data.sync.OfflineWriteException
import com.reviewapp.android.data.sync.OfflineWrites
import com.reviewapp.android.data.sync.OpWait
import com.reviewapp.android.data.sync.OutboxDescriber
import com.reviewapp.android.data.local.offline.EntityType
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

/**
 * Emitted after the current user changed one of their collections or its media. [fromSync]: a sync updated the
 * offline copy instead (doesn't trigger another sync).
 */
data class CollectionChange(val collectionId: Int, val fromSync: Boolean = false)

class CollectionRepository(
    private val collectionApi: CollectionApi,
    private val userApi: UserApi,
    private val sessionManager: SessionManager,
    private val appScope: CoroutineScope,
    private val local: LocalLibrary,
    private val offline: OfflineWrites,
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
            // Changes of the collections wait to be sent: the copy has them as the user left them
            if (local.hasPending(EntityType.COLLECTION)) {
                offlineMine()?.let { mine ->
                    _mine.value = mine
                    return@launch
                }
            }
            val (collections, memberships) = coroutineScope {
                val collections = async { apiCall(useSnapshot = false) { userApi.getCollections(username, DiscoverOptions.DEFAULT_COLLECTION_SORT) } }
                val memberships = async { apiCall(useSnapshot = false) { collectionApi.getMemberships() } }
                collections.await() to memberships.await()
            }
            val failure = collections as? ApiResult.Error ?: memberships as? ApiResult.Error
            // Offline: the hearts and checkmarks come from the offline copy
            val offline = if (failure?.isNetwork == true) offlineMine() else null
            _mine.update { current ->
                when {
                    collections is ApiResult.Success && memberships is ApiResult.Success -> MyCollections(
                        collections = collections.data.map { it.toDomain() },
                        memberships = memberships.data.groupBy({ it.mediaKey }, { it.collectionId }).mapValues { it.value.toSet() },
                        isLoaded = true,
                    )
                    offline != null -> offline
                    else -> current.copy(isLoading = false, error = failure?.message)
                }
            }
        }
    }

    private suspend fun offlineMine(): MyCollections? {
        val reads = local.reads() ?: return null
        return try {
            MyCollections(reads.collections(DiscoverOptions.DEFAULT_COLLECTION_SORT), reads.memberships(), isLoaded = true)
        } catch (_: Exception) {
            null
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
        if (offline.writer.canWrite()) {
            return offlineWrite(collectionId) { offline.writer.setInCollection(collectionId, media, add) }.also { result ->
                if (result is ApiResult.Error) _mine.update { it.withMembership(media.key, collectionId, !add) }
            }
        }
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

    suspend fun userCollections(username: String, sortBy: String): ApiResult<List<MediaCollection>> = local.networkFirst(
        own = local.isMe(username),
        pendingTypes = listOf(EntityType.COLLECTION),
        remote = { useSnapshot -> apiCall(useSnapshot) { userApi.getCollections(username, sortBy) }.map { list -> list.map { it.toDomain() } } },
        local = { collections(sortBy) },
    )

    suspend fun details(id: Int): ApiResult<CollectionDetails> {
        // Created offline: the real id once the backend has it
        val realId = local.resolve(EntityType.COLLECTION, id)
        return local.networkFirst(
            own = local.ownsCollection(realId),
            pendingTypes = listOf(EntityType.COLLECTION),
            remote = { useSnapshot ->
                if (realId < 0) ApiResult.Error("Collection not found.", 404)
                else apiCall(useSnapshot) { collectionApi.getCollection(realId) }.map { it.toDomain() }
            },
            local = { collectionDetails(realId) },
        )
    }

    /** Offline mode: applied to the offline copy and queued (the sync sends it). */
    private suspend fun offlineWrite(collectionId: Int, write: suspend () -> Unit): ApiResult<Unit> = try {
        write()
        _changes.tryEmit(CollectionChange(collectionId))
        ApiResult.Success(Unit)
    } catch (e: OfflineWriteException) {
        ApiResult.Error(e.message ?: "Could not change the collection.")
    }

    /** A sync applied changes: the hearts reload, and so do the lists (temporary ids became real ones). */
    fun onSynced() {
        refresh()
        _changes.tryEmit(CollectionChange(collectionId = 0, fromSync = true))
    }

    /**
     * Offline mode: a name the copy already has is refused at once; online the answer is awaited a moment, so a name
     * taken on the web meanwhile is refused like before (the queued change is thrown away).
     */
    private suspend fun createOffline(name: String, visibility: Visibility): ApiResult<MediaCollection> {
        val (entity, op) = try {
            offline.writer.createCollection(name, visibility)
        } catch (e: OfflineWriteException) {
            return ApiResult.Error(e.message ?: "Could not create the collection.")
        }
        val wait = offline.engine.awaitOp(op.opId)
        if (wait is OpWait.Issue) {
            offline.writer.discard(op.seq)
            return ApiResult.Error(OutboxDescriber.reasonMessage(wait.reason))
        }
        // Applied: the backend's id is in id_map now (the list reloads with it through the sync's event)
        val id = local.resolve(EntityType.COLLECTION, entity.id)
        val collection = MediaCollection(id, entity.name, Visibility.fromId(entity.visibility), entity.createdAt.take(10), 0, isOwner = true, isDefault = false, syncState = entity.syncState)
        _mine.update { it.copy(collections = it.collections + collection) }
        _changes.tryEmit(CollectionChange(collection.id))
        return ApiResult.Success(collection)
    }

    private suspend fun updateOffline(collection: MediaCollection, name: String, visibility: Visibility): ApiResult<MediaCollection> {
        val op = try {
            offline.writer.updateCollection(collection.id, name, visibility)
        } catch (e: OfflineWriteException) {
            return ApiResult.Error(e.message ?: "Could not save the collection.")
        }
        if (offline.engine.awaitOp(op.opId).let { it is OpWait.Issue && it.reason == "name_taken" }) {
            offline.writer.discard(op.seq)
            return ApiResult.Error(OutboxDescriber.reasonMessage("name_taken"))
        }
        val updated = collection.copy(name = name.trim(), visibility = visibility)
        _mine.update { mine -> mine.copy(collections = mine.collections.map { if (it.id == collection.id) updated else it }) }
        _changes.tryEmit(CollectionChange(collection.id))
        return ApiResult.Success(updated)
    }

    suspend fun create(name: String, visibility: Visibility): ApiResult<MediaCollection> {
        if (offline.writer.canWrite()) return createOffline(name, visibility)
        val result = apiCall { collectionApi.create(CreateCollectionRequest(name.trim(), visibility.id)) }
            .map { it.toDomain() }
        if (result is ApiResult.Success) {
            _mine.update { it.copy(collections = it.collections + result.data) }
            _changes.tryEmit(CollectionChange(result.data.id))
        }
        return result
    }

    suspend fun update(collection: MediaCollection, name: String, visibility: Visibility): ApiResult<MediaCollection> {
        if (offline.writer.canWrite()) return updateOffline(collection, name, visibility)
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
        val result = if (offline.writer.canWrite()) {
            offlineWrite(collectionId) { offline.writer.deleteCollection(collectionId) }
        } else {
            apiCall { collectionApi.delete(collectionId) }
        }
        if (result is ApiResult.Success) {
            _mine.update { it.withoutCollection(collectionId) }
            _changes.tryEmit(CollectionChange(collectionId))
        }
        return result
    }

    /**
     * Moves one media to [newIndex] (0-based, the API shifts the rest). Offline mode sends the whole [order] instead
     * (media added offline have no backend id to move yet).
     */
    suspend fun reorder(collectionId: Int, dbMediaId: Int, newIndex: Int, order: List<Media>): ApiResult<Unit> {
        if (offline.writer.canWrite()) return offlineWrite(collectionId) { offline.writer.reorder(collectionId, order) }
        return apiCall { collectionApi.reorder(collectionId, ReorderMediaRequest(dbMediaId, newIndex)) }
    }
}
