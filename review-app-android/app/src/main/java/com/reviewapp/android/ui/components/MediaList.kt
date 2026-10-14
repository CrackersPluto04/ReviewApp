package com.reviewapp.android.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.PagedList

/** Infinite list of [MediaCard]s, shared by search and discover. */
@Composable
fun MediaList(
    list: PagedList<Media>,
    scoreStats: Map<String, ScoreStats>,
    onNeedStats: (Media) -> Unit,
    onOpenMedia: (Media) -> Unit,
    onLoadMore: () -> Unit,
    onRetry: () -> Unit,
    emptyMessage: String,
    modifier: Modifier = Modifier,
    header: (LazyListScope.() -> Unit)? = null,
) {
    val listState = rememberLazyListState()
    InfiniteListEffect(listState, onLoadMore = onLoadMore)

    LazyColumn(
        state = listState,
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = modifier.fillMaxSize(),
    ) {
        header?.invoke(this)
        when {
            list.isInitialLoading -> item { LoadingState() }
            list.items.isEmpty() && list.error != null -> item { ErrorState(list.error, onRetry) }
            list.isEmpty -> item { EmptyState(emptyMessage) }
            else -> {
                items(list.items, key = { it.key }) { media ->
                    MediaCard(
                        media = media,
                        stats = scoreStats[media.key],
                        onNeedStats = onNeedStats,
                        onClick = onOpenMedia,
                    )
                }
                item { ListFooter(list.isLoading, list.error, onRetry) }
            }
        }
    }
}
