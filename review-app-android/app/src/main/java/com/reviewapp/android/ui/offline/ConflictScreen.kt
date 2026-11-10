package com.reviewapp.android.ui.offline

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.data.sync.FieldChoice
import com.reviewapp.android.data.sync.FieldDiff
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.OfflineNotice

/**
 * One change in Sync issues: what happened, both versions where it matters, and the choices (keep mine, use the
 * other one, edit a combination, recreate, rename, merge, try again, discard). Nothing is thrown away before the
 * user picks.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConflictScreen(
    onBack: () -> Unit,
    viewModel: ConflictViewModel = viewModel(factory = ConflictViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val currentOnBack by rememberUpdatedState(onBack)
    LaunchedEffect(viewModel) { viewModel.done.collect { currentOnBack() } }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Resolve change") }, navigationIcon = { BackButton(onBack) }) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        val view = state.view
        when {
            !state.isLoaded -> Box(modifier) { LoadingState() }
            state.gone || view == null -> EmptyState("This change was already synced or discarded.", modifier)
            else -> Column(
                modifier.verticalScroll(rememberScrollState()).imePadding().padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(view.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(view.explanation, style = MaterialTheme.typography.bodyMedium)

                view.diffs.forEach { diff ->
                    FieldChoiceCard(
                        diff = diff,
                        choice = state.choices[diff.field] ?: FieldChoice.Mine,
                        editedText = state.edits[diff.field].orEmpty(),
                        enabled = !state.isWorking,
                        onChoose = { viewModel.choose(diff.field, it) },
                        onEdit = { viewModel.edit(diff.field, it) },
                    )
                }
                if (view.mine != null || view.theirs != null) {
                    view.mine?.let { VersionCard("On this phone", it) }
                    view.theirs?.let { VersionCard("On ReviewApp", it) }
                }
                if (view.currentName != null) {
                    OutlinedTextField(
                        value = state.name,
                        onValueChange = viewModel::setName,
                        label = { Text("New name") },
                        singleLine = true,
                        enabled = !state.isWorking,
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                state.error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium) }
                if (!LocalIsOnline.current) OfflineNotice("Your decision is kept and sent when you're back online.")

                Actions(view, state, viewModel)
            }
        }
    }

    val view = state.view
    if (state.confirmingDiscard && view != null) {
        AlertDialog(
            onDismissRequest = { viewModel.askDiscard(false) },
            title = { Text("${view.discardLabel}?") },
            text = { Text("Your change won't be synced, and later changes of the same thing on this phone go with it. It stays as it is on ReviewApp.") },
            confirmButton = {
                TextButton(onClick = { viewModel.act(IssueAction.UseTheirs) }) { Text(view.discardLabel, color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { viewModel.askDiscard(false) }) { Text("Cancel") } },
        )
    }
}

/** The decisions the issue offers: the main one filled, the others outlined, and the discard last (with a confirmation). */
@Composable
private fun Actions(view: IssueView, state: ConflictUiState, viewModel: ConflictViewModel) {
    Column(Modifier.fillMaxWidth().padding(top = 4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        view.actions.filter { it != IssueAction.UseTheirs }.forEachIndexed { index, action ->
            val enabled = !state.isWorking && (action != IssueAction.Rename || state.name.isNotBlank())
            if (index == 0) {
                Button(onClick = { viewModel.act(action) }, enabled = enabled, modifier = Modifier.fillMaxWidth()) { Text(action.label) }
            } else {
                OutlinedButton(onClick = { viewModel.act(action) }, enabled = enabled, modifier = Modifier.fillMaxWidth()) { Text(action.label) }
            }
        }
        TextButton(
            onClick = { viewModel.askDiscard(true) },
            enabled = !state.isWorking,
            colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
            modifier = Modifier.fillMaxWidth(),
        ) { Text(view.discardLabel) }
    }
}

/** Mine / theirs (/ an edit for texts) for one field both sides changed. */
@Composable
private fun FieldChoiceCard(
    diff: FieldDiff,
    choice: FieldChoice,
    editedText: String,
    enabled: Boolean,
    onChoose: (FieldChoice) -> Unit,
    onEdit: (String) -> Unit,
) {
    OutlinedCard(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(vertical = 8.dp).selectableGroup()) {
            Text(diff.label, style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
            ChoiceRow("Yours", diff.mine, choice == FieldChoice.Mine, enabled) { onChoose(FieldChoice.Mine) }
            ChoiceRow("On ReviewApp", diff.theirs, choice == FieldChoice.Theirs, enabled) { onChoose(FieldChoice.Theirs) }
            if (diff.isText) {
                val editing = choice is FieldChoice.Edited
                ChoiceRow("Edit a combination", null, editing, enabled) { onChoose(FieldChoice.Edited(editedText)) }
                if (editing) {
                    OutlinedTextField(
                        value = editedText,
                        onValueChange = onEdit,
                        enabled = enabled,
                        supportingText = { Text("${editedText.length} / ${diff.maxLength}") },
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                    )
                }
            }
        }
    }
}

/** One radio choice with its label and, when given, the value it stands for. */
@Composable
private fun ChoiceRow(label: String, value: String?, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .selectable(selected = selected, enabled = enabled, role = Role.RadioButton, onClick = onClick)
            .padding(horizontal = 4.dp, vertical = 2.dp),
        verticalAlignment = Alignment.Top,
    ) {
        RadioButton(selected = selected, onClick = null, enabled = enabled, modifier = Modifier.padding(12.dp))
        Column(Modifier.weight(1f).padding(top = 10.dp, end = 12.dp, bottom = 6.dp)) {
            Text(label, style = MaterialTheme.typography.labelLarge)
            if (value != null) {
                Text(
                    value.ifBlank { "(empty)" },
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 6,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

/** A version of a review (this phone's or the backend's) as score, visibility and text. */
@Composable
private fun VersionCard(title: String, version: ReviewVersion) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall)
            Text("Score ${version.score} · ${version.visibility}", style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            version.text?.let { Text(it, style = MaterialTheme.typography.bodyMedium, maxLines = 6, overflow = TextOverflow.Ellipsis) }
        }
    }
}
