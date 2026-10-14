package com.reviewapp.android.ui.home

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PersonSearch
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.ui.components.AppLogo
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.FeedReviewCard
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.PosterTile
import com.reviewapp.android.ui.components.SearchField
import com.reviewapp.android.ui.components.SectionTitle
import com.reviewapp.android.ui.notifications.NotificationBell

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    onSearch: (String) -> Unit,
    onOpenMedia: (Media) -> Unit,
    onOpenReview: (FeedReview) -> Unit,
    onOpenReviewMedia: (FeedReview) -> Unit,
    onOpenProfile: (String) -> Unit,
    onOpenUserSearch: () -> Unit,
    /** Null while logged out, then there is no bell. */
    unseenNotifications: Int?,
    onOpenNotifications: () -> Unit,
    viewModel: HomeViewModel = viewModel(factory = HomeViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var query by rememberSaveable { mutableStateOf("") }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { AppLogo() },
                actions = {
                    IconButton(onClick = onOpenUserSearch) {
                        Icon(Icons.Filled.PersonSearch, contentDescription = "Find users")
                    }
                    if (unseenNotifications != null) NotificationBell(unseenNotifications, onOpenNotifications)
                },
            )
        },
    ) { padding ->
        PullToRefreshBox(
            isRefreshing = state.isRefreshing,
            onRefresh = viewModel::refresh,
            modifier = Modifier.fillMaxSize().padding(padding),
        ) {
            LazyColumn(
                contentPadding = PaddingValues(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                item {
                    Text(
                        "Welcome to THE Review App!",
                        style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                    )
                }
                item {
                    SearchField(
                        value = query,
                        onValueChange = { query = it },
                        onSearch = { if (query.isNotBlank()) onSearch(query.trim()) },
                        modifier = Modifier.padding(horizontal = 16.dp),
                    )
                }
                carousel("Popular movies", state.popularMovies, onOpenMedia)
                carousel("Popular series", state.popularSeries, onOpenMedia)

                item { SectionTitle("Recent reviews", Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) }
                val reviews = state.recentReviews
                when {
                    reviews.isLoading -> item { LoadingState() }
                    reviews.error != null -> item { ErrorState(reviews.error, viewModel::refresh, Modifier.fillMaxWidth()) }
                    reviews.items.isEmpty() -> item { EmptyState("No reviews yet. Be the first to write one!") }
                    else -> items(reviews.items, key = { "review-${it.id}" }) { review ->
                        FeedReviewCard(
                            review = review,
                            onClick = { onOpenReview(review) },
                            onOpenMedia = { onOpenReviewMedia(review) },
                            onOpenProfile = onOpenProfile,
                            modifier = Modifier.padding(horizontal = 16.dp),
                        )
                    }
                }
            }
        }
    }
}

private fun LazyListScope.carousel(title: String, section: Section<Media>, onOpenMedia: (Media) -> Unit) {
    item { SectionTitle(title, Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) }
    item {
        when {
            section.isLoading -> Box(Modifier.height(220.dp).fillMaxWidth()) { LoadingState() }
            section.error != null -> ErrorState(section.error, onRetry = null, modifier = Modifier.fillMaxWidth())
            else -> LazyRow(
                contentPadding = PaddingValues(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                items(section.items, key = { it.key }) { media -> PosterTile(media, onOpenMedia) }
            }
        }
    }
}
