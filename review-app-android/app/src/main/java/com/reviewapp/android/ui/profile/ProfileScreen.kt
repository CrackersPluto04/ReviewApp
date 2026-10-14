package com.reviewapp.android.ui.profile

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.BrightnessAuto
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.DarkMode
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.LightMode
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.PrimaryTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.data.local.ThemeMode
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.UserProfile
import com.reviewapp.android.ui.PagedList
import com.reviewapp.android.ui.achievements.AchievementFilterChips
import com.reviewapp.android.ui.achievements.AchievementRow
import com.reviewapp.android.ui.collection.CollectionFormDialog
import com.reviewapp.android.ui.collection.FavouriteRed
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.DropdownFilterChip
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.FilterChipRow
import com.reviewapp.android.ui.components.InfiniteListEffect
import com.reviewapp.android.ui.components.LimitedTextField
import com.reviewapp.android.ui.components.ListFooter
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.ProfileReviewCard
import com.reviewapp.android.ui.components.ReviewFilterChips
import com.reviewapp.android.ui.components.ReviewsTitle
import com.reviewapp.android.ui.components.ScoreRangeSheet
import com.reviewapp.android.ui.components.UserAvatar
import com.reviewapp.android.ui.components.icon
import com.reviewapp.android.ui.settings.EmailVerificationBanner
import com.reviewapp.android.ui.settings.VerificationBannerViewModel
import com.reviewapp.android.util.AchievementFilter
import com.reviewapp.android.util.DiscoverOptions
import com.reviewapp.android.util.Validation
import com.reviewapp.android.util.formatDate
import com.reviewapp.android.util.earnedTiers
import com.reviewapp.android.util.pluralize
import com.reviewapp.android.util.totalTiers

/**
 * A user's profile (the web's `ProfileLayout` with the Reviews, Collections and Achievements tabs).
 * Also the Profile tab, where it shows the logged-in user or a login prompt.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProfileScreen(
    onBack: (() -> Unit)?,
    onLogin: () -> Unit,
    onOpenMedia: (MediaType, String) -> Unit,
    onEditReview: (MediaType, String) -> Unit,
    onOpenReview: (Int) -> Unit,
    onOpenFollowList: (username: String, following: Boolean) -> Unit,
    onOpenCollection: (Int) -> Unit,
    onOpenSettings: () -> Unit,
    viewModel: ProfileViewModel = viewModel(factory = ProfileViewModel.Factory),
    bannerViewModel: VerificationBannerViewModel = viewModel(factory = VerificationBannerViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val banner by bannerViewModel.state.collectAsStateWithLifecycle()
    val reviews by viewModel.reviews.collectAsStateWithLifecycle()
    val themeMode by viewModel.themeMode.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val listState = rememberLazyListState()
    var showScoreSheet by rememberSaveable { mutableStateOf(false) }
    val currentOnLogin by rememberUpdatedState(onLogin)

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is ProfileEvent.Message -> snackbarHostState.showSnackbar(event.text)
                ProfileEvent.LoginRequired -> currentOnLogin()
            }
        }
    }
    InfiniteListEffect(listState) {
        if (state.tab == ProfileTab.Reviews) viewModel.loadMoreReviews()
    }

    val profile = state.profile
    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    if (profile != null && !state.showLoginPrompt) {
                        Column {
                            Text(profile.username, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            formatDate(profile.joinedAt)?.let {
                                Text(
                                    "Joined $it",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    } else {
                        Text("Profile")
                    }
                },
                navigationIcon = { if (onBack != null) BackButton(onBack) },
                actions = {
                    ThemeMenu(themeMode, viewModel::setThemeMode)
                    if (state.isOwner) {
                        IconButton(onClick = onOpenSettings) { Icon(Icons.Outlined.Settings, contentDescription = "Account settings") }
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            state.showLoginPrompt -> LoginPrompt(onLogin, modifier)
            profile == null && state.isLoading -> Box(modifier) { LoadingState() }
            profile == null -> Box(modifier) {
                if (state.notFound) {
                    EmptyState("User not found.")
                } else {
                    ErrorState(state.error ?: "Could not load this profile.", viewModel::reloadAll, Modifier.fillMaxWidth())
                }
            }
            else -> LazyColumn(
                state = listState,
                contentPadding = PaddingValues(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
                modifier = modifier,
            ) {
                if (state.isOwner && banner.visible) {
                    item(key = "verify-email") {
                        EmailVerificationBanner(bannerViewModel, Modifier.padding(horizontal = 16.dp).padding(top = 8.dp))
                    }
                }
                item(key = "header") {
                    ProfileHeader(
                        profile = profile,
                        isOwner = state.isOwner,
                        isFollowBusy = state.isFollowBusy,
                        onToggleFollow = viewModel::toggleFollow,
                        onOpenFollowList = { following -> onOpenFollowList(profile.username, following) },
                        onEditAvatar = viewModel::openAvatarDialog,
                    )
                }
                item(key = "bio") {
                    BioCard(
                        profile = profile,
                        isOwner = state.isOwner,
                        draft = state.edits.bioDraft,
                        isSaving = state.edits.isSavingBio,
                        onEdit = viewModel::startEditBio,
                        onDraftChange = viewModel::updateBioDraft,
                        onCancel = viewModel::cancelEditBio,
                        onSave = viewModel::saveBio,
                    )
                }
                // One sticky item: separate sticky headers would push each other away instead of stacking
                stickyHeader(key = "tabs") {
                    TabsHeader(
                        state = state,
                        totalReviews = reviews.totalCount,
                        onSelectTab = viewModel::selectTab,
                        onFilterChange = viewModel::updateReviewFilter,
                        onOpenScoreFilter = { showScoreSheet = true },
                        onSortCollections = viewModel::updateCollectionSort,
                        onCreateCollection = { viewModel.showCreateCollection(true) },
                        onAchievementFilterChange = viewModel::updateAchievementFilter,
                    )
                }
                when (state.tab) {
                    ProfileTab.Reviews -> reviewItems(
                        reviews = reviews,
                        filterIsDefault = state.reviewFilter.isDefault,
                        onOpenMedia = onOpenMedia,
                        onEditReview = onEditReview,
                        onOpenReview = onOpenReview,
                        viewModel = viewModel,
                    )
                    ProfileTab.Collections -> collectionItems(state.collections, viewModel::retryCollections, onOpenCollection)
                    ProfileTab.Achievements -> achievementItems(
                        state.achievements,
                        onRetry = viewModel::retryAchievements,
                        onResetFilter = { viewModel.updateAchievementFilter(AchievementFilter()) },
                    )
                }
            }
        }
    }

    if (showScoreSheet) {
        ScoreRangeSheet(
            filter = state.reviewFilter,
            onApply = {
                viewModel.updateReviewFilter(it)
                showScoreSheet = false
            },
            onDismiss = { showScoreSheet = false },
        )
    }
    ProfileDialogs(state, viewModel)
}

private fun LazyListScope.reviewItems(
    reviews: PagedList<ProfileReview>,
    filterIsDefault: Boolean,
    onOpenMedia: (MediaType, String) -> Unit,
    onEditReview: (MediaType, String) -> Unit,
    onOpenReview: (Int) -> Unit,
    viewModel: ProfileViewModel,
) {
    when {
        reviews.isInitialLoading -> item { LoadingState() }
        reviews.items.isEmpty() && reviews.error != null -> item { ErrorState(reviews.error, viewModel::retryReviews) }
        reviews.isEmpty -> item {
            EmptyState(if (filterIsDefault) "No reviews yet." else "No reviews match these filters.")
        }
        else -> {
            items(reviews.items, key = { "review-${it.id}" }) { review ->
                ProfileReviewCard(
                    review = review,
                    onClick = { onOpenReview(review.id) },
                    onOpenMedia = { onOpenMedia(review.mediaType, review.externalApiId) },
                    onEdit = { onEditReview(review.mediaType, review.externalApiId) },
                    onDelete = { viewModel.askDeleteReview(review) },
                    modifier = Modifier.padding(horizontal = 16.dp),
                )
            }
            item { ListFooter(reviews.isLoading, reviews.error, viewModel::retryReviews) }
        }
    }
}

private fun LazyListScope.collectionItems(
    collections: CollectionsState,
    onRetry: () -> Unit,
    onOpenCollection: (Int) -> Unit,
) {
    when {
        collections.items.isEmpty() && collections.isLoading -> item { LoadingState() }
        collections.items.isEmpty() && collections.error != null -> item { ErrorState(collections.error, onRetry) }
        collections.items.isEmpty() -> item { EmptyState("No collections yet.") }
        else -> items(collections.items, key = { "collection-${it.id}" }) { collection ->
            CollectionCard(collection, onClick = { onOpenCollection(collection.id) })
        }
    }
}

private fun LazyListScope.achievementItems(
    achievements: AchievementsState,
    onRetry: () -> Unit,
    onResetFilter: () -> Unit,
) {
    val shown = achievements.shown
    when {
        achievements.items.isEmpty() && (achievements.isLoading || !achievements.loaded && achievements.error == null) ->
            item { LoadingState() }
        achievements.items.isEmpty() && achievements.error != null -> item { ErrorState(achievements.error, onRetry) }
        achievements.items.isEmpty() -> item { EmptyState("No achievements yet.") }
        shown.isEmpty() -> item {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                EmptyState("No achievements match these filters.")
                OutlinedButton(onClick = onResetFilter) { Text("Reset filters") }
            }
        }
        else -> items(shown, key = { "achievement-${it.groupCode}" }) { achievement ->
            AchievementRow(achievement, Modifier.padding(horizontal = 16.dp).animateItem())
        }
    }
}

@Composable
private fun LoginPrompt(onLogin: () -> Unit, modifier: Modifier) {
    Box(modifier.padding(16.dp)) {
        Card(Modifier.fillMaxWidth()) {
            Column(
                Modifier.padding(16.dp).fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text("You're not logged in", style = MaterialTheme.typography.titleMedium)
                Text(
                    "Log in to see your profile, reviews and collections.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Button(onClick = onLogin, modifier = Modifier.fillMaxWidth()) { Text("Login / Register") }
            }
        }
    }
}

@Composable
private fun ProfileHeader(
    profile: UserProfile,
    isOwner: Boolean,
    isFollowBusy: Boolean,
    onToggleFollow: () -> Unit,
    onOpenFollowList: (following: Boolean) -> Unit,
    onEditAvatar: () -> Unit,
) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box {
                UserAvatar(profile.username, profile.profilePictureUrl, size = 88.dp)
                if (isOwner) {
                    FilledTonalIconButton(
                        onClick = onEditAvatar,
                        modifier = Modifier.size(32.dp).align(Alignment.BottomEnd),
                    ) {
                        Icon(Icons.Filled.Edit, contentDescription = "Change profile picture", modifier = Modifier.size(16.dp))
                    }
                }
            }
            Row(Modifier.weight(1f), horizontalArrangement = Arrangement.SpaceEvenly) {
                CountButton(profile.followersCount, "Followers") { onOpenFollowList(false) }
                CountButton(profile.followingCount, "Following") { onOpenFollowList(true) }
            }
        }
        if (!isOwner) {
            if (profile.isFollowed) {
                OutlinedButton(onClick = onToggleFollow, enabled = !isFollowBusy, modifier = Modifier.fillMaxWidth()) {
                    Text("Unfollow")
                }
            } else {
                Button(onClick = onToggleFollow, enabled = !isFollowBusy, modifier = Modifier.fillMaxWidth()) {
                    Text("Follow")
                }
            }
        }
    }
}

@Composable
private fun CountButton(count: Int, label: String, onClick: () -> Unit) {
    Column(
        Modifier
            .clip(MaterialTheme.shapes.medium)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(count.toString(), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun BioCard(
    profile: UserProfile,
    isOwner: Boolean,
    draft: String?,
    isSaving: Boolean,
    onEdit: () -> Unit,
    onDraftChange: (String) -> Unit,
    onCancel: () -> Unit,
    onSave: () -> Unit,
) {
    Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        if (draft != null) {
            Column(Modifier.padding(12.dp)) {
                LimitedTextField(
                    value = draft,
                    onValueChange = onDraftChange,
                    label = "Bio",
                    maxLength = Validation.MAX_BIO_LENGTH,
                    minLines = 3,
                    placeholder = "Write something about yourself...",
                )
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    TextButton(onClick = onCancel, enabled = !isSaving) { Text("Cancel") }
                    Spacer(Modifier.width(8.dp))
                    Button(onClick = onSave, enabled = !isSaving && draft.length <= Validation.MAX_BIO_LENGTH) {
                        if (isSaving) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Save")
                    }
                }
            }
        } else {
            Row(Modifier.padding(start = 16.dp, top = 8.dp, bottom = 8.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                val empty = profile.bio.isBlank()
                Text(
                    if (empty) "Welcome to ${profile.username}'s profile!" else profile.bio,
                    style = MaterialTheme.typography.bodyMedium,
                    fontStyle = if (empty) FontStyle.Italic else FontStyle.Normal,
                    color = if (empty) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f).padding(vertical = 8.dp),
                )
                if (isOwner) {
                    IconButton(onClick = onEdit) { Icon(Icons.Outlined.Edit, contentDescription = "Edit bio") }
                }
            }
        }
    }
}

@Composable
private fun TabsHeader(
    state: ProfileUiState,
    totalReviews: Int,
    onSelectTab: (ProfileTab) -> Unit,
    onFilterChange: (ReviewFilter) -> Unit,
    onOpenScoreFilter: () -> Unit,
    onSortCollections: (String) -> Unit,
    onCreateCollection: () -> Unit,
    onAchievementFilterChange: (AchievementFilter) -> Unit,
) {
    Surface(color = MaterialTheme.colorScheme.background, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(bottom = 8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            PrimaryTabRow(selectedTabIndex = state.tab.ordinal, containerColor = MaterialTheme.colorScheme.background) {
                ProfileTab.entries.forEach { tab ->
                    Tab(
                        selected = state.tab == tab,
                        onClick = { onSelectTab(tab) },
                        text = { Text(tab.label) },
                        unselectedContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            when (state.tab) {
                ProfileTab.Reviews -> {
                    ReviewsTitle(totalReviews)
                    ReviewFilterChips(state.reviewFilter, onFilterChange, onOpenScoreFilter)
                }
                ProfileTab.Collections -> Row(
                    Modifier.fillMaxWidth().padding(end = 16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    FilterChipRow(Modifier.weight(1f).padding(horizontal = 16.dp)) {
                        DropdownFilterChip(
                            label = "Sort",
                            options = DiscoverOptions.collectionSortOptions,
                            selected = state.collections.sortBy,
                            onSelect = { onSortCollections(it ?: DiscoverOptions.DEFAULT_COLLECTION_SORT) },
                            highlighted = state.collections.sortBy != DiscoverOptions.DEFAULT_COLLECTION_SORT,
                        )
                    }
                    if (state.isOwner) {
                        FilledTonalIconButton(onClick = onCreateCollection) {
                            Icon(Icons.Filled.Add, contentDescription = "New collection")
                        }
                    }
                }
                ProfileTab.Achievements -> {
                    val items = state.achievements.items
                    if (items.isNotEmpty()) {
                        Text(
                            "${earnedTiers(items)} of ${totalTiers(items)} tiers earned",
                            style = MaterialTheme.typography.titleSmall,
                            modifier = Modifier.padding(horizontal = 16.dp),
                        )
                    }
                    AchievementFilterChips(state.achievements.filter, onAchievementFilterChange)
                }
            }
        }
    }
}

@Composable
private fun CollectionCard(collection: MediaCollection, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(
                    collection.name,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    pluralize(collection.mediaCount, "media", "media"),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            when {
                collection.isDefault -> Icon(Icons.Filled.Favorite, contentDescription = "Favourites", tint = FavouriteRed)
                collection.isOwner -> Icon(
                    collection.visibility.icon,
                    contentDescription = collection.visibility.label,
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null)
        }
    }
}

@Composable
private fun ThemeMenu(mode: ThemeMode, onSelect: (ThemeMode) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { expanded = true }) { Icon(mode.icon, contentDescription = "Theme") }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            ThemeMode.entries.forEach { option ->
                DropdownMenuItem(
                    text = { Text(option.label) },
                    leadingIcon = { Icon(option.icon, null) },
                    trailingIcon = { if (option == mode) Icon(Icons.Filled.Check, null) },
                    onClick = {
                        expanded = false
                        onSelect(option)
                    },
                )
            }
        }
    }
}

private val ThemeMode.icon: ImageVector
    get() = when (this) {
        ThemeMode.System -> Icons.Filled.BrightnessAuto
        ThemeMode.Light -> Icons.Filled.LightMode
        ThemeMode.Dark -> Icons.Filled.DarkMode
    }

/** Account settings (username, email, password, 2FA) come in a later milestone; logout for now. */
@Composable
private fun ProfileDialogs(state: ProfileUiState, viewModel: ProfileViewModel) {
    val edits = state.edits
    edits.avatarDraft?.let { draft ->
        AlertDialog(
            onDismissRequest = { if (!edits.isSavingAvatar) viewModel.closeAvatarDialog() },
            title = { Text("Profile picture") },
            text = {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    UserAvatar(
                        state.profile?.username.orEmpty(),
                        draft.trim().takeIf { Validation.isValidImageUrl(it) },
                        size = 96.dp,
                    )
                    OutlinedTextField(
                        value = draft,
                        onValueChange = viewModel::updateAvatarDraft,
                        label = { Text("Image URL") },
                        supportingText = { Text(edits.avatarError ?: "Paste a direct link to an image.") },
                        isError = edits.avatarError != null,
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = viewModel::saveAvatar, enabled = !edits.isSavingAvatar && draft.isNotBlank()) {
                    if (edits.isSavingAvatar) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Save")
                }
            },
            dismissButton = {
                TextButton(onClick = viewModel::closeAvatarDialog, enabled = !edits.isSavingAvatar) { Text("Cancel") }
            },
        )
    }

    edits.reviewToDelete?.let { review ->
        AlertDialog(
            onDismissRequest = { if (!edits.isDeletingReview) viewModel.askDeleteReview(null) },
            title = { Text("Delete review?") },
            text = { Text("Your review of ${review.mediaTitle} will be deleted with its replies. This can't be undone.") },
            confirmButton = {
                TextButton(onClick = viewModel::deleteReview, enabled = !edits.isDeletingReview) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(onClick = { viewModel.askDeleteReview(null) }, enabled = !edits.isDeletingReview) { Text("Cancel") }
            },
        )
    }

    if (edits.showCreateCollection) {
        CollectionFormDialog(
            title = "New collection",
            confirmLabel = "Create",
            isSaving = edits.isCreatingCollection,
            error = edits.createCollectionError,
            onConfirm = viewModel::createCollection,
            onDismiss = { viewModel.showCreateCollection(false) },
        )
    }
}
