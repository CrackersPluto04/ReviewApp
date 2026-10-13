package com.reviewapp.android.ui.review

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.Poster
import com.reviewapp.android.ui.theme.color
import com.reviewapp.android.util.MAX_SCORE
import com.reviewapp.android.util.MIN_SCORE
import com.reviewapp.android.util.Validation
import com.reviewapp.android.util.formatDate
import com.reviewapp.android.util.formatScore
import com.reviewapp.android.util.scoreTier
import com.reviewapp.android.util.snapScore

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WriteReviewScreen(
    onClose: () -> Unit,
    viewModel: WriteReviewViewModel = viewModel(factory = WriteReviewViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    LaunchedEffect(viewModel) {
        viewModel.saved.collect { onClose() }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(if (state.isEdit) "Edit your review" else "Write your review")
                        if (state.isEdit) {
                            Text(
                                "You already reviewed this, saving updates it",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                },
                navigationIcon = {
                    IconButton(onClick = onClose) { Icon(Icons.Filled.Close, contentDescription = "Close") }
                },
            )
        },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            state.isLoading -> Box(modifier) { LoadingState() }
            state.loadError != null -> Box(modifier) { ErrorState(state.loadError!!, viewModel::load, Modifier.fillMaxWidth()) }
            else -> ReviewForm(state, viewModel, modifier)
        }
    }
}

@Composable
private fun ReviewForm(state: WriteReviewUiState, viewModel: WriteReviewViewModel, modifier: Modifier) {
    val draft = state.draft
    Column(
        modifier
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        state.media?.let { MediaSummary(it) }

        ScoreSlider(score = draft.score, onScoreChange = { score -> viewModel.updateDraft { copy(score = score) } })

        LimitedTextField(
            value = draft.reviewText,
            onValueChange = { text -> viewModel.updateDraft { copy(reviewText = text) } },
            label = "What did you think?",
            maxLength = Validation.MAX_REVIEW_LENGTH,
            minLines = 5,
        )
        LimitedTextField(
            value = draft.pros,
            onValueChange = { text -> viewModel.updateDraft { copy(pros = text) } },
            label = "Pros",
            maxLength = Validation.MAX_PROS_CONS_LENGTH,
            minLines = 2,
        )
        LimitedTextField(
            value = draft.cons,
            onValueChange = { text -> viewModel.updateDraft { copy(cons = text) } },
            label = "Cons",
            maxLength = Validation.MAX_PROS_CONS_LENGTH,
            minLines = 2,
        )

        VisibilityField(draft.visibility) { visibility -> viewModel.updateDraft { copy(visibility = visibility) } }

        state.saveError?.let {
            Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
        }

        Button(
            onClick = viewModel::save,
            enabled = state.canSave,
            modifier = Modifier.fillMaxWidth().height(52.dp),
        ) {
            if (state.isSaving) {
                CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp)
            } else {
                Text(if (state.isEdit) "Save changes" else "Save review", style = MaterialTheme.typography.titleMedium)
            }
        }
    }
}

@Composable
private fun MediaSummary(media: Media) {
    Card(Modifier.fillMaxWidth()) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Poster(media.posterUrl, media.mediaType, Modifier.size(width = 56.dp, height = 84.dp))
            Spacer(Modifier.width(12.dp))
            Column {
                Text(media.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    listOfNotNull(media.mediaType.label, formatDate(media.releaseDate)).joinToString(" · "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                if (media.creator.isNotBlank()) Text(media.creator, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

@Composable
private fun ScoreSlider(score: Double, onScoreChange: (Double) -> Unit) {
    val tier = scoreTier(score)
    Column {
        Row(verticalAlignment = Alignment.Bottom) {
            Text("Score", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.weight(1f))
            Text(
                "${formatScore(score)} / 10",
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.Bold,
                color = tier.color(),
            )
            Spacer(Modifier.width(8.dp))
            Text(tier.label, style = MaterialTheme.typography.titleSmall, color = tier.color())
        }
        Slider(
            value = score.toFloat(),
            onValueChange = { onScoreChange(snapScore(it.toDouble())) },
            valueRange = MIN_SCORE.toFloat()..MAX_SCORE.toFloat(),
            // 1-10 in steps of 0.5 is 18 intervals, so 17 stops between the ends
            steps = 17,
            colors = SliderDefaults.colors(
                thumbColor = tier.color(),
                activeTrackColor = tier.color(),
                activeTickColor = tier.color(),
                inactiveTickColor = MaterialTheme.colorScheme.onSurfaceVariant,
            ),
        )
    }
}

/** Text field with a live "n / max" counter that turns red over the limit (like the web form). */
@Composable
private fun LimitedTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    maxLength: Int,
    minLines: Int,
) {
    val tooLong = value.length > maxLength
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        minLines = minLines,
        isError = tooLong,
        supportingText = {
            Text(
                "${value.length} / $maxLength",
                modifier = Modifier.fillMaxWidth(),
                textAlign = TextAlign.End,
            )
        },
        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun VisibilityField(selected: Visibility, onSelect: (Visibility) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedTextField(
            value = selected.label,
            onValueChange = {},
            readOnly = true,
            label = { Text("Visibility") },
            trailingIcon = { Icon(Icons.Filled.ArrowDropDown, null) },
            modifier = Modifier.fillMaxWidth(),
        )
        // A read-only text field still takes the touch, so a transparent layer opens the menu
        Box(Modifier.matchParentSize().clickable { expanded = true })
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            Visibility.entries.forEach { visibility ->
                DropdownMenuItem(
                    text = { Text(visibility.label) },
                    onClick = {
                        expanded = false
                        onSelect(visibility)
                    },
                )
            }
        }
    }
}
