package com.reviewapp.android.ui.search

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.PrimaryTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.MediaCard
import com.reviewapp.android.ui.components.MediaList
import com.reviewapp.android.ui.components.SearchField
import com.reviewapp.android.ui.components.SectionTitle

@Composable
fun SearchScreen(
    onBack: () -> Unit,
    onOpenMedia: (Media) -> Unit,
    viewModel: SearchViewModel = viewModel(factory = SearchViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val scoreStats by viewModel.scoreStats.collectAsStateWithLifecycle()
    val focusManager = LocalFocusManager.current

    Scaffold(
        topBar = {
            // Unlike TopAppBar, a custom top bar has to handle the status bar itself
            Column(Modifier.statusBarsPadding().padding(top = 8.dp)) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.fillMaxWidth().padding(end = 16.dp),
                ) {
                    BackButton(onBack)
                    SearchField(
                        value = state.draftQuery,
                        onValueChange = viewModel::onDraftQueryChange,
                        onSearch = {
                            focusManager.clearFocus()
                            viewModel.submit()
                        },
                        label = "Search results for",
                        modifier = Modifier.weight(1f),
                    )
                }
                PrimaryTabRow(selectedTabIndex = state.tab.ordinal) {
                    SearchTab.entries.forEach { tab ->
                        Tab(
                            selected = state.tab == tab,
                            onClick = { viewModel.selectTab(tab) },
                            text = { Text(tab.label) },
                        )
                    }
                }
            }
        },
    ) { padding ->
        val contentModifier = Modifier.fillMaxSize().padding(padding)
        val tab = state.tab
        if (tab == SearchTab.Any) {
            SearchAllContent(
                all = state.all,
                scoreStats = scoreStats,
                onNeedStats = viewModel::requestStats,
                onOpenMedia = onOpenMedia,
                onSeeAll = viewModel::selectTab,
                onRetry = viewModel::retryAll,
                modifier = contentModifier,
            )
        } else {
            val list by viewModel.listState(tab).collectAsStateWithLifecycle()
            MediaList(
                list = list,
                scoreStats = scoreStats,
                onNeedStats = viewModel::requestStats,
                onOpenMedia = onOpenMedia,
                onLoadMore = { viewModel.loadMore(tab) },
                onRetry = { viewModel.retry(tab) },
                emptyMessage = "No ${tab.label.lowercase()} found for \"${state.query}\".",
                modifier = contentModifier,
            )
        }
    }
}

/** Up to 5 results of each type, grouped, with a "See all" link to the type's tab. */
@Composable
private fun SearchAllContent(
    all: SearchAllState,
    scoreStats: Map<String, ScoreStats>,
    onNeedStats: (Media) -> Unit,
    onOpenMedia: (Media) -> Unit,
    onSeeAll: (SearchTab) -> Unit,
    onRetry: () -> Unit,
    modifier: Modifier,
) {
    LazyColumn(
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = modifier,
    ) {
        when {
            all.isLoading -> item { LoadingState() }
            all.error != null -> item { ErrorState(all.error, onRetry) }
            all.items.isEmpty() -> item { EmptyState("No results found.") }
            else -> SearchTab.entries.filter { it.mediaType != null }.forEach { tab ->
                val group = all.items.filter { it.mediaType == tab.mediaType }
                if (group.isEmpty()) return@forEach
                item(key = "header-${tab.name}") {
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                        SectionTitle(tab.label, Modifier.weight(1f))
                        TextButton(onClick = { onSeeAll(tab) }) {
                            Text("See all ${tab.label}", style = MaterialTheme.typography.labelLarge)
                        }
                    }
                }
                items(group, key = { it.key }) { media ->
                    MediaCard(media, scoreStats[media.key], onNeedStats, onOpenMedia)
                }
            }
        }
    }
}
