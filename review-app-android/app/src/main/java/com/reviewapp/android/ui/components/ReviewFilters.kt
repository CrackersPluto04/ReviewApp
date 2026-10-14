package com.reviewapp.android.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.RangeSlider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.util.DiscoverOptions
import com.reviewapp.android.util.MAX_SCORE
import com.reviewapp.android.util.MIN_SCORE
import com.reviewapp.android.util.formatScore
import com.reviewapp.android.util.snapScore

// Review list filters shared by the media details and profile review lists. Changes apply at once.

/** "Reviews (N)" with the filter chips, meant as a sticky header (opaque background). */
@Composable
fun ReviewsHeader(
    totalCount: Int,
    filter: ReviewFilter,
    onFilterChange: (ReviewFilter) -> Unit,
    onOpenScoreFilter: () -> Unit,
) {
    Surface(color = MaterialTheme.colorScheme.background, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(vertical = 8.dp)) {
            ReviewsTitle(totalCount)
            ReviewFilterChips(filter, onFilterChange, onOpenScoreFilter)
        }
    }
}

@Composable
fun ReviewsTitle(totalCount: Int) {
    Text(
        "Reviews ($totalCount)",
        style = MaterialTheme.typography.titleMedium,
        fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(horizontal = 16.dp),
    )
}

/** Sort, score range (opens [ScoreRangeSheet]), "Written only" and Clear. */
@Composable
fun ReviewFilterChips(
    filter: ReviewFilter,
    onFilterChange: (ReviewFilter) -> Unit,
    onOpenScoreFilter: () -> Unit,
) {
    FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
        DropdownFilterChip(
            label = "Sort",
            options = DiscoverOptions.reviewSortOptions,
            selected = filter.sortBy,
            onSelect = { onFilterChange(filter.copy(sortBy = it ?: DiscoverOptions.DEFAULT_REVIEW_SORT)) },
            highlighted = filter.sortBy != DiscoverOptions.DEFAULT_REVIEW_SORT,
        )
        val scoreFiltered = filter.minScore != MIN_SCORE || filter.maxScore != MAX_SCORE
        FilterChip(
            selected = scoreFiltered,
            onClick = onOpenScoreFilter,
            label = {
                Text(
                    if (scoreFiltered) "Score ${formatScore(filter.minScore)}-${formatScore(filter.maxScore)}"
                    else "Score",
                )
            },
        )
        FilterChip(
            selected = filter.writtenOnly,
            onClick = { onFilterChange(filter.copy(writtenOnly = !filter.writtenOnly)) },
            label = { Text("Written only") },
        )
        if (!filter.isDefault) {
            TextButton(onClick = { onFilterChange(ReviewFilter()) }) { Text("Clear") }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ScoreRangeSheet(filter: ReviewFilter, onApply: (ReviewFilter) -> Unit, onDismiss: () -> Unit) {
    var range by remember { mutableStateOf(filter.minScore.toFloat()..filter.maxScore.toFloat()) }
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp)) {
            Text("Score range", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            Text(
                "${formatScore(snapScore(range.start.toDouble()))} - ${formatScore(snapScore(range.endInclusive.toDouble()))}",
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.align(Alignment.CenterHorizontally).padding(top = 8.dp),
            )
            RangeSlider(
                value = range,
                onValueChange = { range = it },
                valueRange = MIN_SCORE.toFloat()..MAX_SCORE.toFloat(),
                // 1-10 in steps of 0.5 is 18 intervals, so 17 stops between the ends
                steps = 17,
            )
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = { range = MIN_SCORE.toFloat()..MAX_SCORE.toFloat() }) { Text("Reset") }
                Spacer(Modifier.width(8.dp))
                Button(onClick = {
                    onApply(
                        filter.copy(
                            minScore = snapScore(range.start.toDouble()),
                            maxScore = snapScore(range.endInclusive.toDouble()),
                        ),
                    )
                }) { Text("Apply") }
            }
        }
    }
}
