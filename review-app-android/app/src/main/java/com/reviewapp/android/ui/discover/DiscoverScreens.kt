package com.reviewapp.android.ui.discover

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material3.AssistChip
import androidx.compose.material3.AssistChipDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.data.repository.TmdbDiscoverFilters
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.ui.components.DropdownFilterChip
import com.reviewapp.android.ui.components.FilterChipRow
import com.reviewapp.android.ui.components.MediaList
import com.reviewapp.android.ui.components.NumberFieldsDialog
import com.reviewapp.android.util.DiscoverOptions

private const val MIN_YEAR = 1900
private const val MAX_YEAR = 2100

private fun yearError(year: Int?): String? =
    if (year != null && year !in MIN_YEAR..MAX_YEAR) "Enter a year between $MIN_YEAR and $MAX_YEAR." else null

/** Movies or series tab: TMDb discover with sort, genre, year and runtime filters. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DiscoverScreen(
    mediaType: MediaType,
    onOpenMedia: (Media) -> Unit,
    viewModel: TmdbDiscoverViewModel = viewModel(
        key = "discover-${mediaType.name}",
        factory = TmdbDiscoverViewModel.factory(mediaType),
    ),
) {
    val filters by viewModel.filters.collectAsStateWithLifecycle()
    val list by viewModel.list.collectAsStateWithLifecycle()
    val scoreStats by viewModel.scoreStats.collectAsStateWithLifecycle()
    var dialog by rememberSaveable { mutableStateOf<String?>(null) }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Discover ${if (mediaType == MediaType.Series) "Series" else "Movies"}") }) },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
                DropdownFilterChip(
                    label = "Sort by",
                    options = DiscoverOptions.tmdbSortOptions(mediaType),
                    selected = filters.sortBy,
                    onSelect = { viewModel.updateFilters(filters.copy(sortBy = it ?: DiscoverOptions.DEFAULT_TMDB_SORT)) },
                    highlighted = filters.sortBy != DiscoverOptions.DEFAULT_TMDB_SORT,
                )
                DropdownFilterChip(
                    label = "Genre",
                    options = DiscoverOptions.tmdbGenres(mediaType),
                    selected = filters.genreId,
                    onSelect = { viewModel.updateFilters(filters.copy(genreId = it)) },
                    allLabel = "Any genre",
                )
                DialogChip(
                    label = filters.year?.toString() ?: "Year",
                    active = filters.year != null,
                    onClick = { dialog = "year" },
                )
                DialogChip(
                    label = runtimeLabel(filters),
                    active = filters.minRuntime != null || filters.maxRuntime != null,
                    onClick = { dialog = "runtime" },
                )
                if (filters != viewModel.defaultFilters) ClearChip(viewModel::clearFilters)
            }

            MediaList(
                list = list,
                scoreStats = scoreStats,
                onNeedStats = viewModel::requestStats,
                onOpenMedia = onOpenMedia,
                onLoadMore = viewModel::loadMore,
                onRetry = viewModel::retry,
                emptyMessage = "Nothing matches these filters.",
            )
        }
    }

    when (dialog) {
        "year" -> NumberFieldsDialog(
            title = "Release year",
            fields = listOf("Year" to filters.year),
            validate = { (year) -> yearError(year) },
            onConfirm = { (year) ->
                viewModel.updateFilters(filters.copy(year = year))
                dialog = null
            },
            onDismiss = { dialog = null },
        )
        "runtime" -> NumberFieldsDialog(
            title = "Runtime (minutes)",
            fields = listOf("Min runtime" to filters.minRuntime, "Max runtime" to filters.maxRuntime),
            validate = { (min, max) ->
                if (min != null && max != null && min > max) "Min runtime can't be more than max runtime." else null
            },
            onConfirm = { (min, max) ->
                viewModel.updateFilters(filters.copy(minRuntime = min, maxRuntime = max))
                dialog = null
            },
            onDismiss = { dialog = null },
        )
    }
}

private fun runtimeLabel(filters: TmdbDiscoverFilters): String {
    val min = filters.minRuntime
    val max = filters.maxRuntime
    return when {
        min != null && max != null -> "$min-$max min"
        min != null -> "$min+ min"
        max != null -> "≤ $max min"
        else -> "Runtime"
    }
}

/** Music tab: Spotify search by genre, year range and market. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DiscoverMusicScreen(
    onOpenMedia: (Media) -> Unit,
    viewModel: MusicDiscoverViewModel = viewModel(factory = MusicDiscoverViewModel.Factory),
) {
    val filters by viewModel.filters.collectAsStateWithLifecycle()
    val list by viewModel.list.collectAsStateWithLifecycle()
    val scoreStats by viewModel.scoreStats.collectAsStateWithLifecycle()
    var showYearDialog by rememberSaveable { mutableStateOf(false) }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Discover Music") }) },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
                DropdownFilterChip(
                    label = "Genre",
                    options = DiscoverOptions.musicGenres,
                    selected = filters.genre,
                    onSelect = { viewModel.updateFilters(filters.copy(genre = it ?: DiscoverOptions.DEFAULT_MUSIC_GENRE)) },
                    highlighted = filters.genre != DiscoverOptions.DEFAULT_MUSIC_GENRE,
                )
                DialogChip(
                    label = filters.yearParam ?: "Year",
                    active = filters.yearParam != null,
                    onClick = { showYearDialog = true },
                )
                DropdownFilterChip(
                    label = "Market",
                    options = DiscoverOptions.markets,
                    selected = filters.market,
                    onSelect = { viewModel.updateFilters(filters.copy(market = it ?: DiscoverOptions.DEFAULT_MARKET)) },
                    highlighted = filters.market != DiscoverOptions.DEFAULT_MARKET,
                )
                if (filters != viewModel.defaultFilters) ClearChip(viewModel::clearFilters)
            }

            MediaList(
                list = list,
                scoreStats = scoreStats,
                onNeedStats = viewModel::requestStats,
                onOpenMedia = onOpenMedia,
                onLoadMore = viewModel::loadMore,
                onRetry = viewModel::retry,
                emptyMessage = "Nothing matches these filters.",
            )
        }
    }

    if (showYearDialog) {
        NumberFieldsDialog(
            title = "Release year",
            fields = listOf("From (year)" to filters.yearFrom, "To (year)" to filters.yearTo),
            validate = { (from, to) ->
                yearError(from) ?: yearError(to)
                    ?: if (from != null && to != null && from > to) "\"From\" can't be after \"To\"." else null
            },
            onConfirm = { (from, to) ->
                viewModel.updateFilters(filters.copy(yearFrom = from, yearTo = to))
                showYearDialog = false
            },
            onDismiss = { showYearDialog = false },
        )
    }
}

@Composable
private fun DialogChip(label: String, active: Boolean, onClick: () -> Unit) {
    FilterChip(
        selected = active,
        onClick = onClick,
        label = { Text(label) },
        trailingIcon = { Icon(Icons.Filled.ArrowDropDown, null, Modifier.size(FilterChipDefaults.IconSize)) },
    )
}

@Composable
private fun ClearChip(onClick: () -> Unit) {
    AssistChip(
        onClick = onClick,
        label = { Text("Clear") },
        leadingIcon = { Icon(Icons.Filled.Clear, null, Modifier.size(AssistChipDefaults.IconSize)) },
    )
}
