package com.reviewapp.android.ui.profile

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.local.ThemeMode
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.UserRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.PagedLoader
import com.reviewapp.android.ui.navigation.ProfileRoute
import com.reviewapp.android.util.DiscoverOptions
import com.reviewapp.android.util.Validation
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class ProfileTab(val label: String) { Reviews("Reviews"), Collections("Collections") }

data class CollectionsState(
    val items: List<MediaCollection> = emptyList(),
    val sortBy: String = DiscoverOptions.DEFAULT_COLLECTION_SORT,
    val isLoading: Boolean = false,
    val error: String? = null,
)

/** One of the owner's dialogs/edits that is open on the profile. */
data class ProfileEdits(
    val bioDraft: String? = null,
    val isSavingBio: Boolean = false,
    val avatarDraft: String? = null,
    val isSavingAvatar: Boolean = false,
    val avatarError: String? = null,
    val reviewToDelete: ProfileReview? = null,
    val isDeletingReview: Boolean = false,
    val showCreateCollection: Boolean = false,
    val isCreatingCollection: Boolean = false,
    val createCollectionError: String? = null,
)

data class ProfileUiState(
    /** The Profile tab of a logged-out user: shows the login prompt instead of a profile. */
    val showLoginPrompt: Boolean = false,
    val isLoggedIn: Boolean = false,
    val currentUserId: Int? = null,
    val profile: UserProfile? = null,
    val isLoading: Boolean = true,
    val error: String? = null,
    val notFound: Boolean = false,
    val tab: ProfileTab = ProfileTab.Reviews,
    val reviewFilter: ReviewFilter = ReviewFilter(),
    val collections: CollectionsState = CollectionsState(),
    val isFollowBusy: Boolean = false,
    val edits: ProfileEdits = ProfileEdits(),
) {
    val isOwner: Boolean get() = profile != null && profile.id == currentUserId
}

sealed interface ProfileEvent {
    data class Message(val text: String) : ProfileEvent
    data object LoginRequired : ProfileEvent
}

/**
 * A user's profile with the Reviews and Collections tabs. [fixedUsername] is the route's user; when
 * it's null (the Profile tab) it follows the logged-in user and shows a login prompt when logged out.
 */
class ProfileViewModel(
    private val fixedUsername: String?,
    private val sessionManager: SessionManager,
    private val userRepository: UserRepository,
    private val reviewRepository: ReviewRepository,
    private val collectionRepository: CollectionRepository,
    private val settingsStore: SettingsStore,
) : ViewModel() {

    private val _state = MutableStateFlow(ProfileUiState())
    val state: StateFlow<ProfileUiState> = _state.asStateFlow()

    val themeMode: StateFlow<ThemeMode> = settingsStore.themeMode
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), ThemeMode.System)

    private val _events = Channel<ProfileEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    private var username: String? = fixedUsername

    private val reviewsLoader = PagedLoader<ProfileReview>(viewModelScope, itemKey = { it.id }) { page ->
        val user = username ?: return@PagedLoader ApiResult.Error("No user.")
        userRepository.reviews(user, _state.value.reviewFilter, page)
    }
    val reviews: StateFlow<PagedList<ProfileReview>> = reviewsLoader.state

    private var profileJob: Job? = null
    private var collectionsJob: Job? = null

    init {
        // What the profile shows depends on who is looking (follow state, private/followers-only
        // content), so everything reloads when the logged-in user changes
        viewModelScope.launch {
            sessionManager.state.distinctUntilChanged { old, new -> old.userId == new.userId && old::class == new::class }
                .collect { auth -> onAuthChanged(auth) }
        }
        viewModelScope.launch {
            // Counts changed (here or in a follower list), refresh them without a loading state
            userRepository.followChanges.collect { loadProfile(quiet = true) }
        }
        viewModelScope.launch {
            // Created/edited from the review form; deletions are removed locally instead
            reviewRepository.changes.collect { change ->
                if (_state.value.isOwner && !change.deleted) reviewsLoader.reload()
            }
        }
        viewModelScope.launch {
            collectionRepository.changes.collect { if (_state.value.isOwner) loadCollections() }
        }
    }

    private val AuthState.userId: Int? get() = (this as? AuthState.LoggedIn)?.user?.id

    private fun onAuthChanged(auth: AuthState) {
        val user = (auth as? AuthState.LoggedIn)?.user
        _state.update { it.copy(isLoggedIn = user != null, currentUserId = user?.id, edits = ProfileEdits()) }
        if (fixedUsername != null) {
            if (auth != AuthState.Loading) reloadAll()
            return
        }
        when (auth) {
            AuthState.Loading -> _state.update { it.copy(isLoading = true, showLoginPrompt = false) }
            AuthState.LoggedOut -> {
                username = null
                profileJob?.cancel()
                collectionsJob?.cancel()
                reviewsLoader.reset()
                _state.value = ProfileUiState(showLoginPrompt = true, isLoading = false)
            }
            is AuthState.LoggedIn -> {
                username = auth.user.username
                _state.update { it.copy(showLoginPrompt = false) }
                reloadAll()
            }
        }
    }

    fun reloadAll() {
        loadProfile(quiet = false)
        reviewsLoader.reload()
        loadCollections()
    }

    private fun loadProfile(quiet: Boolean) {
        val user = username ?: return
        profileJob?.cancel()
        if (!quiet) _state.update { it.copy(isLoading = true, error = null, notFound = false) }
        profileJob = viewModelScope.launch {
            when (val result = userRepository.profile(user)) {
                is ApiResult.Success -> _state.update { it.copy(profile = result.data, isLoading = false, error = null) }
                is ApiResult.Error -> if (!quiet) {
                    _state.update {
                        it.copy(isLoading = false, error = result.message, notFound = result.code == 404, profile = null)
                    }
                }
            }
        }
    }

    private fun loadCollections() {
        val user = username ?: return
        collectionsJob?.cancel()
        _state.update { it.copy(collections = it.collections.copy(isLoading = true, error = null)) }
        collectionsJob = viewModelScope.launch {
            val result = collectionRepository.userCollections(user, _state.value.collections.sortBy)
            _state.update {
                it.copy(
                    collections = when (result) {
                        is ApiResult.Success -> it.collections.copy(items = result.data, isLoading = false)
                        is ApiResult.Error -> it.collections.copy(isLoading = false, error = result.message)
                    },
                )
            }
        }
    }

    fun selectTab(tab: ProfileTab) = _state.update { it.copy(tab = tab) }

    fun updateReviewFilter(filter: ReviewFilter) {
        if (filter == _state.value.reviewFilter) return
        _state.update { it.copy(reviewFilter = filter) }
        reviewsLoader.reload()
    }

    fun loadMoreReviews() = reviewsLoader.loadMore()
    fun retryReviews() = reviewsLoader.retry()

    fun updateCollectionSort(sortBy: String) {
        if (sortBy == _state.value.collections.sortBy) return
        _state.update { it.copy(collections = it.collections.copy(sortBy = sortBy)) }
        loadCollections()
    }

    fun retryCollections() = loadCollections()

    // Follow

    /** Optimistic like the web: the button and the count change at once and are put back on failure. */
    fun toggleFollow() {
        val current = _state.value
        val profile = current.profile ?: return
        if (!current.isLoggedIn) {
            _events.trySend(ProfileEvent.LoginRequired)
            return
        }
        if (current.isFollowBusy) return
        val follow = !profile.isFollowed
        setFollowed(follow)
        _state.update { it.copy(isFollowBusy = true) }
        viewModelScope.launch {
            val result = userRepository.setFollowing(profile.id, follow)
            _state.update { it.copy(isFollowBusy = false) }
            when (result) {
                is ApiResult.Success -> {
                    // Followers-only reviews and collections appear or disappear
                    reviewsLoader.reload()
                    loadCollections()
                }
                is ApiResult.Error -> {
                    setFollowed(!follow)
                    _events.send(ProfileEvent.Message(result.message))
                }
            }
        }
    }

    private fun setFollowed(followed: Boolean) = _state.update { state ->
        val profile = state.profile ?: return@update state
        if (profile.isFollowed == followed) return@update state
        state.copy(
            profile = profile.copy(
                isFollowed = followed,
                followersCount = (profile.followersCount + if (followed) 1 else -1).coerceAtLeast(0),
            ),
        )
    }

    // Bio

    fun startEditBio() = updateEdits { it.copy(bioDraft = _state.value.profile?.bio.orEmpty()) }
    fun updateBioDraft(text: String) = updateEdits { it.copy(bioDraft = text) }
    fun cancelEditBio() = updateEdits { it.copy(bioDraft = null) }

    fun saveBio() {
        val draft = _state.value.edits.bioDraft ?: return
        if (draft.length > Validation.MAX_BIO_LENGTH) return
        val bio = draft.trim()
        updateEdits { it.copy(isSavingBio = true) }
        viewModelScope.launch {
            when (val result = userRepository.updateBio(bio)) {
                is ApiResult.Success -> _state.update { state ->
                    state.copy(
                        profile = state.profile?.copy(bio = bio),
                        edits = state.edits.copy(bioDraft = null, isSavingBio = false),
                    )
                }
                is ApiResult.Error -> {
                    updateEdits { it.copy(isSavingBio = false) }
                    _events.send(ProfileEvent.Message(result.message))
                }
            }
        }
    }

    // Avatar

    fun openAvatarDialog() = updateEdits { it.copy(avatarDraft = _state.value.profile?.profilePictureUrl.orEmpty(), avatarError = null) }
    fun updateAvatarDraft(url: String) = updateEdits { it.copy(avatarDraft = url, avatarError = null) }
    fun closeAvatarDialog() = updateEdits { it.copy(avatarDraft = null, avatarError = null) }

    fun saveAvatar() {
        val url = _state.value.edits.avatarDraft?.trim() ?: return
        if (!Validation.isValidImageUrl(url)) {
            updateEdits { it.copy(avatarError = "Enter a direct http(s) link to an image.") }
            return
        }
        updateEdits { it.copy(isSavingAvatar = true, avatarError = null) }
        viewModelScope.launch {
            when (val result = userRepository.updateAvatar(url)) {
                is ApiResult.Success -> _state.update { state ->
                    state.copy(
                        profile = state.profile?.copy(profilePictureUrl = url),
                        edits = state.edits.copy(avatarDraft = null, isSavingAvatar = false),
                    )
                }
                is ApiResult.Error -> updateEdits { it.copy(isSavingAvatar = false, avatarError = result.message) }
            }
        }
    }

    // Reviews

    fun askDeleteReview(review: ProfileReview?) = updateEdits { it.copy(reviewToDelete = review) }

    fun deleteReview() {
        val review = _state.value.edits.reviewToDelete ?: return
        updateEdits { it.copy(isDeletingReview = true) }
        viewModelScope.launch {
            val result = reviewRepository.delete(review)
            updateEdits { it.copy(isDeletingReview = false, reviewToDelete = null) }
            when (result) {
                is ApiResult.Success -> {
                    reviewsLoader.remove(review.id)
                    _events.send(ProfileEvent.Message("Review deleted."))
                }
                is ApiResult.Error -> _events.send(ProfileEvent.Message(result.message))
            }
        }
    }

    // Collections

    fun showCreateCollection(show: Boolean) =
        updateEdits { it.copy(showCreateCollection = show, createCollectionError = null) }

    fun createCollection(name: String, visibility: Visibility) {
        updateEdits { it.copy(isCreatingCollection = true, createCollectionError = null) }
        viewModelScope.launch {
            // The list reloads through CollectionRepository.changes
            when (val result = collectionRepository.create(name, visibility)) {
                is ApiResult.Success -> updateEdits { it.copy(isCreatingCollection = false, showCreateCollection = false) }
                is ApiResult.Error -> updateEdits {
                    it.copy(isCreatingCollection = false, createCollectionError = result.message)
                }
            }
        }
    }

    // Top bar

    fun setThemeMode(mode: ThemeMode) {
        viewModelScope.launch { settingsStore.setThemeMode(mode) }
    }

    fun logout() {
        viewModelScope.launch { sessionManager.logout() }
    }

    private fun updateEdits(transform: (ProfileEdits) -> ProfileEdits) =
        _state.update { it.copy(edits = transform(it.edits)) }

    companion object {
        /** For [ProfileRoute] the username comes from the route, the Profile tab has none. */
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                val username = createSavedStateHandle().get<String>(ProfileRoute::username.name)
                ProfileViewModel(
                    username, c.sessionManager, c.userRepository, c.reviewRepository, c.collectionRepository, c.settingsStore,
                )
            }
        }
    }
}
