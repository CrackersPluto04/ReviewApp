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
import com.reviewapp.android.data.repository.AchievementRepository
import com.reviewapp.android.data.repository.CollectionRepository
import com.reviewapp.android.data.repository.ReplyRepository
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.data.repository.ReviewRepository
import com.reviewapp.android.data.repository.UserRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.PagedLoader
import com.reviewapp.android.ui.navigation.ProfileRoute
import com.reviewapp.android.util.AchievementFilter
import com.reviewapp.android.util.DiscoverOptions
import com.reviewapp.android.util.Validation
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** The tabs of a profile; the name is what a route or notification passes to open on one. */
enum class ProfileTab(val label: String) {
    Reviews("Reviews"), Collections("Collections"), Achievements("Achievements");

    companion object {
        /** The tab with this enum name, or `null` for an unknown or missing name. */
        fun fromName(name: String?): ProfileTab? = entries.firstOrNull { it.name == name }
    }
}

/** Loaded the first time the tab is opened; filtered and sorted locally like on the web. */
data class AchievementsState(
    val items: List<Achievement> = emptyList(),
    val filter: AchievementFilter = AchievementFilter(),
    val isLoading: Boolean = false,
    val error: String? = null,
    /** The first load finished, so the tab is not loaded again on every visit. */
    val loaded: Boolean = false,
) {
    /** The achievements after the filter and sort. */
    val shown: List<Achievement> get() = filter.apply(items)
}

/** The Collections tab's list and its sort. */
data class CollectionsState(
    val items: List<MediaCollection> = emptyList(),
    val sortBy: String = DiscoverOptions.DEFAULT_COLLECTION_SORT,
    val isLoading: Boolean = false,
    val error: String? = null,
)

/** One of the owner's dialogs/edits that is open on the profile. */
data class ProfileEdits(
    /** The bio being edited inline; `null` when not editing. */
    val bioDraft: String? = null,
    val isSavingBio: Boolean = false,
    /** The picture URL in the dialog; `null` when the dialog is closed. */
    val avatarDraft: String? = null,
    val isSavingAvatar: Boolean = false,
    val avatarError: String? = null,
    val reviewToDelete: ProfileReview? = null,
    val isDeletingReview: Boolean = false,
    val showCreateCollection: Boolean = false,
    val isCreatingCollection: Boolean = false,
    val createCollectionError: String? = null,
)

/** Everything the profile screen shows: the profile, the tab and its filters, and the owner's edits. */
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
    val achievements: AchievementsState = AchievementsState(),
    /** A follow / unfollow request is running; further taps are ignored. */
    val isFollowBusy: Boolean = false,
    val edits: ProfileEdits = ProfileEdits(),
) {
    /** The logged-in user is looking at their own profile (owner-only actions show). */
    val isOwner: Boolean get() = profile != null && profile.id == currentUserId
}

/** One-off results of the profile screen. */
sealed interface ProfileEvent {
    /** A message for the snackbar. */
    data class Message(val text: String) : ProfileEvent

    /** A logged-out user tried something that needs a login; the screen opens the login. */
    data object LoginRequired : ProfileEvent
}

/**
 * A user's profile with the Reviews, Collections and Achievements tabs. [fixedUsername] is the route's
 * user; when it's null (the Profile tab) it follows the logged-in user and shows a login prompt when
 * logged out. [initialTab] opens the profile on that tab (e.g. from an achievement notification).
 */
class ProfileViewModel(
    private val fixedUsername: String?,
    initialTab: ProfileTab?,
    private val sessionManager: SessionManager,
    private val userRepository: UserRepository,
    private val reviewRepository: ReviewRepository,
    private val collectionRepository: CollectionRepository,
    private val replyRepository: ReplyRepository,
    private val achievementRepository: AchievementRepository,
    private val settingsStore: SettingsStore,
) : ViewModel() {

    private val _state = MutableStateFlow(ProfileUiState(tab = initialTab ?: ProfileTab.Reviews))
    val state: StateFlow<ProfileUiState> = _state.asStateFlow()

    /** The theme setting, shown in the top bar's menu. */
    val themeMode: StateFlow<ThemeMode> = settingsStore.themeMode
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), ThemeMode.System)

    private val _events = Channel<ProfileEvent>(Channel.BUFFERED)
    val events = _events.receiveAsFlow()

    /** The profile being shown; for the Profile tab it is the logged-in user's current name. */
    private var username: String? = fixedUsername

    private val reviewsLoader = PagedLoader<ProfileReview>(viewModelScope, itemKey = { it.id }) { page ->
        val user = username ?: return@PagedLoader ApiResult.Error("No user.")
        userRepository.reviews(user, _state.value.reviewFilter, page)
    }
    val reviews: StateFlow<PagedList<ProfileReview>> = reviewsLoader.state

    private var profileJob: Job? = null
    private var collectionsJob: Job? = null
    private var achievementsJob: Job? = null

    init {
        // What the profile shows depends on who is looking (follow state, private/followers-only
        // content), so everything reloads when the logged-in user changes
        viewModelScope.launch {
            sessionManager.state.distinctUntilChanged { old, new -> old.userId == new.userId && old::class == new::class }
                .collect { auth -> onAuthChanged(auth) }
        }
        if (fixedUsername == null) {
            viewModelScope.launch {
                // Renamed in the account settings: the Profile tab follows the new name
                sessionManager.state.collect { auth ->
                    val user = (auth as? AuthState.LoggedIn)?.user ?: return@collect
                    val current = username
                    if (current != null && user.id == _state.value.currentUserId && user.username != current) {
                        username = user.username
                        reloadAll()
                    }
                }
            }
        }
        viewModelScope.launch {
            // Counts changed (here or in a follower list), refresh them without a loading state
            userRepository.followChanges.collect { loadProfile(quiet = true) }
        }
        viewModelScope.launch {
            // Subscribed or Premium ended: the owner's badge follows it
            sessionManager.state
                .map { auth -> (auth as? AuthState.LoggedIn)?.user?.let { it.id to it.isPremium } }
                .distinctUntilChanged()
                .drop(1)
                .collect { if (_state.value.isOwner) loadProfile(quiet = true) }
        }
        viewModelScope.launch {
            // Created/edited from the review form reloads; deletions are removed locally
            reviewRepository.changes.collect { change ->
                when {
                    // Deleted on the review page (deleting here already removed it)
                    change.deleted -> change.reviewId?.let { reviewsLoader.remove(it) }
                    _state.value.isOwner -> reviewsLoader.reload()
                }
            }
        }
        viewModelScope.launch {
            collectionRepository.changes.collect { if (_state.value.isOwner) loadCollections() }
        }
        viewModelScope.launch {
            // Replies written on the review page update the counts without a reload
            replyRepository.replyCountChanges.collect { change ->
                reviewsLoader.update(change.reviewId) { it.copy(replyCount = (it.replyCount + change.delta).coerceAtLeast(0)) }
            }
        }
        viewModelScope.launch {
            // The owner just earned a tier (the unlock banner shows it), refresh the progress
            achievementRepository.unlocks.collect {
                if (_state.value.isOwner && _state.value.achievements.loaded) loadAchievements()
            }
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
                achievementsJob?.cancel()
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

    /** Reloads the profile, the reviews and the collections; the achievements reload when their tab is open. */
    fun reloadAll() {
        loadProfile(quiet = false)
        reviewsLoader.reload()
        loadCollections()
        // Achievements load on demand, only when their tab is open (or opens later)
        achievementsJob?.cancel()
        updateAchievements { AchievementsState(filter = it.filter) }
        if (_state.value.tab == ProfileTab.Achievements) loadAchievements()
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

    /** Opens a tab; the achievements load the first time. */
    fun selectTab(tab: ProfileTab) {
        _state.update { it.copy(tab = tab) }
        val achievements = _state.value.achievements
        if (tab == ProfileTab.Achievements && !achievements.loaded && !achievements.isLoading) loadAchievements()
    }

    /** Applies a new filter to the reviews tab and restarts its list. */
    fun updateReviewFilter(filter: ReviewFilter) {
        if (filter == _state.value.reviewFilter) return
        _state.update { it.copy(reviewFilter = filter) }
        reviewsLoader.reload()
    }

    fun loadMoreReviews() = reviewsLoader.loadMore()
    fun retryReviews() = reviewsLoader.retry()

    /** Changes the collections' sort and reloads them. */
    fun updateCollectionSort(sortBy: String) {
        if (sortBy == _state.value.collections.sortBy) return
        _state.update { it.copy(collections = it.collections.copy(sortBy = sortBy)) }
        loadCollections()
    }

    fun retryCollections() = loadCollections()

    private fun loadAchievements() {
        val user = username ?: return
        achievementsJob?.cancel()
        updateAchievements { it.copy(isLoading = true, error = null) }
        achievementsJob = viewModelScope.launch {
            when (val result = achievementRepository.achievements(user)) {
                is ApiResult.Success -> updateAchievements { it.copy(items = result.data, isLoading = false, loaded = true) }
                is ApiResult.Error -> updateAchievements { it.copy(isLoading = false, error = result.message) }
            }
        }
    }

    fun retryAchievements() = loadAchievements()

    /** The achievements filter is applied locally, no request is made. */
    fun updateAchievementFilter(filter: AchievementFilter) = updateAchievements { it.copy(filter = filter) }

    private fun updateAchievements(transform: (AchievementsState) -> AchievementsState) =
        _state.update { it.copy(achievements = transform(it.achievements)) }

    /**
     * Follows or unfollows the shown user. Optimistic like the web: the button and the count change at once and are
     * put back on failure. A logged-out user gets [ProfileEvent.LoginRequired] instead.
     */
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
            val result = userRepository.setFollowing(profile.id, follow, profile.username, profile.profilePictureUrl)
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

    /** The bio edit: [startEditBio] opens it with the current text, [saveBio] sends the trimmed draft. */
    fun startEditBio() = updateEdits { it.copy(bioDraft = _state.value.profile?.bio.orEmpty()) }
    fun updateBioDraft(text: String) = updateEdits { it.copy(bioDraft = text) }
    fun cancelEditBio() = updateEdits { it.copy(bioDraft = null) }

    /** Saves the bio draft unless it is over the length limit. */
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

    /** The picture dialog: [openAvatarDialog] prefills the current URL, [saveAvatar] validates and sends it. */
    fun openAvatarDialog() = updateEdits { it.copy(avatarDraft = _state.value.profile?.profilePictureUrl.orEmpty(), avatarError = null) }
    fun updateAvatarDraft(url: String) = updateEdits { it.copy(avatarDraft = url, avatarError = null) }
    fun closeAvatarDialog() = updateEdits { it.copy(avatarDraft = null, avatarError = null) }

    /** Saves the picture URL; a link that is not a direct http(s) image URL is refused before any request. */
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

    /** Asks for confirmation to delete [review]; `null` cancels. */
    fun askDeleteReview(review: ProfileReview?) = updateEdits { it.copy(reviewToDelete = review) }

    /** Deletes the confirmed review and removes it from the list locally. */
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

    fun showCreateCollection(show: Boolean) =
        updateEdits { it.copy(showCreateCollection = show, createCollectionError = null) }

    /** Creates a collection from the dialog; the list reloads through [CollectionRepository.changes]. */
    fun createCollection(name: String, visibility: Visibility) {
        updateEdits { it.copy(isCreatingCollection = true, createCollectionError = null) }
        viewModelScope.launch {
            when (val result = collectionRepository.create(name, visibility)) {
                is ApiResult.Success -> updateEdits { it.copy(isCreatingCollection = false, showCreateCollection = false) }
                is ApiResult.Error -> updateEdits {
                    it.copy(isCreatingCollection = false, createCollectionError = result.message)
                }
            }
        }
    }

    /** Stores the theme setting. */
    fun setThemeMode(mode: ThemeMode) {
        viewModelScope.launch { settingsStore.setThemeMode(mode) }
    }

    private fun updateEdits(transform: (ProfileEdits) -> ProfileEdits) =
        _state.update { it.copy(edits = transform(it.edits)) }

    companion object {
        /** For [ProfileRoute] the username (and maybe a tab) comes from the route, the Profile tab has none. */
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                val savedState = createSavedStateHandle()
                ProfileViewModel(
                    savedState.get<String>(ProfileRoute::username.name),
                    ProfileTab.fromName(savedState.get<String>(ProfileRoute::tab.name)),
                    c.sessionManager, c.userRepository, c.reviewRepository, c.collectionRepository,
                    c.replyRepository, c.achievementRepository, c.settingsStore,
                )
            }
        }
    }
}
