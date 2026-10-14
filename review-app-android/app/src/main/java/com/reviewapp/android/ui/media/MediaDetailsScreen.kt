package com.reviewapp.android.ui.media

import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.RateReview
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.collection.MediaCollectionButtons
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.InfiniteListEffect
import com.reviewapp.android.ui.components.ListFooter
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.Poster
import com.reviewapp.android.ui.components.ReviewCard
import com.reviewapp.android.ui.components.ReviewsHeader
import com.reviewapp.android.ui.components.ScoreRangeSheet
import com.reviewapp.android.ui.components.ScoreStatsText
import com.reviewapp.android.util.formatDate

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MediaDetailsScreen(
    onBack: () -> Unit,
    onWriteReview: (MediaType, String) -> Unit,
    onLoginRequired: () -> Unit,
    onOpenProfile: (String) -> Unit,
    viewModel: MediaDetailsViewModel = viewModel(factory = MediaDetailsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val stats by viewModel.stats.collectAsStateWithLifecycle()
    val reviews by viewModel.reviews.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val listState = rememberLazyListState()
    var showScoreSheet by rememberSaveable { mutableStateOf(false) }

    LaunchedEffect(viewModel) {
        viewModel.messages.collect { snackbarHostState.showSnackbar(it) }
    }
    InfiniteListEffect(listState, onLoadMore = viewModel::loadMoreReviews)

    Scaffold(
        topBar = { TopAppBar(title = { Text("Media Details") }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        floatingActionButton = {
            if (state.media != null) {
                val ownReview = state.ownReview
                ExtendedFloatingActionButton(
                    text = { Text(if (ownReview == OwnReviewState.Exists) "Edit review" else "Write review") },
                    icon = {
                        Icon(if (ownReview == OwnReviewState.Exists) Icons.Filled.Edit else Icons.Filled.RateReview, null)
                    },
                    onClick = {
                        when (ownReview) {
                            OwnReviewState.LoggedOut -> onLoginRequired()
                            else -> onWriteReview(viewModel.mediaType, viewModel.externalApiId)
                        }
                    },
                )
            }
        },
    ) { padding ->
        LazyColumn(
            state = listState,
            // Room at the bottom so the last review isn't hidden behind the button
            contentPadding = PaddingValues(bottom = 96.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier.fillMaxSize().padding(padding),
        ) {
            val media = state.media
            when {
                media != null -> {
                    item { MediaHeader(media, stats) }
                    if (media.overview.isNotBlank()) item { ExpandableOverview(media.overview) }
                }
                state.isLoadingMedia -> item { LoadingState() }
                else -> item { ErrorState(state.mediaError ?: "Could not load this media.", viewModel::loadMedia) }
            }

            stickyHeader {
                ReviewsHeader(
                    totalCount = reviews.totalCount,
                    filter = state.filter,
                    onFilterChange = viewModel::updateFilter,
                    onOpenScoreFilter = { showScoreSheet = true },
                )
            }

            when {
                reviews.isInitialLoading -> item { LoadingState() }
                reviews.items.isEmpty() && reviews.error != null ->
                    item { ErrorState(reviews.error!!, viewModel::retryReviews) }
                reviews.isEmpty -> item {
                    EmptyState(
                        if (state.filter.isDefault) "No reviews yet. Be the first to write one!"
                        else "No reviews match these filters.",
                    )
                }
                else -> {
                    items(reviews.items, key = { it.id }) { review ->
                        ReviewCard(review, onOpenProfile, Modifier.padding(horizontal = 16.dp))
                    }
                    item { ListFooter(reviews.isLoading, reviews.error, viewModel::retryReviews) }
                }
            }
        }
    }

    if (showScoreSheet) {
        ScoreRangeSheet(
            filter = state.filter,
            onApply = {
                viewModel.updateFilter(it)
                showScoreSheet = false
            },
            onDismiss = { showScoreSheet = false },
        )
    }
}

@Composable
private fun MediaHeader(media: Media, stats: ScoreStats?) {
    Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
        Poster(media.posterUrl, media.mediaType, Modifier.size(width = 130.dp, height = 195.dp))
        Spacer(Modifier.width(16.dp))
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(media.title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            Text(
                listOfNotNull(media.mediaType.label, formatDate(media.releaseDate)).joinToString(" · "),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (media.creator.isNotBlank()) {
                Text(media.creator, style = MaterialTheme.typography.bodyMedium)
            }
            Spacer(Modifier.height(4.dp))
            Text("Average score", style = MaterialTheme.typography.labelMedium)
            ScoreStatsText(stats)
            // IconButtons have their own padding, so pull them back in line with the text
            MediaCollectionButtons(media, Modifier.offset(x = (-12).dp))
        }
    }
}

@Composable
private fun ExpandableOverview(overview: String) {
    var expanded by rememberSaveable { mutableStateOf(false) }
    var overflows by remember { mutableStateOf(false) }
    // Only clickable when there is more to show (a disabled Card would grey out the text)
    val expandable = overflows || expanded
    Card(
        Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp)
            .then(if (expandable) Modifier.clickable { expanded = !expanded } else Modifier),
    ) {
        Column(Modifier.padding(12.dp).animateContentSize()) {
            Text("Overview", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            Text(
                overview,
                style = MaterialTheme.typography.bodyMedium,
                maxLines = if (expanded) Int.MAX_VALUE else 4,
                overflow = TextOverflow.Ellipsis,
                onTextLayout = { if (!expanded) overflows = it.hasVisualOverflow },
            )
            if (expandable) {
                Text(
                    if (expanded) "Show less" else "Show more",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        }
    }
}
