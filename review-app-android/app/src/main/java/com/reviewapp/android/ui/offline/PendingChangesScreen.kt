package com.reviewapp.android.ui.offline

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.theme.AppTheme

/**
 * The changes made on this phone that the backend hasn't accepted yet, in plain language and in the order they
 * were made, each with where it is and Discard (the thing goes back to the backend's version).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PendingChangesScreen(
    onBack: () -> Unit,
    onOpenIssue: (seq: Long) -> Unit,
    viewModel: PendingChangesViewModel = viewModel(factory = PendingChangesViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    LaunchedEffect(viewModel) { viewModel.messages.collect { snackbarHostState.showSnackbar(it) } }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Waiting changes") },
                navigationIcon = { BackButton(onBack) },
                actions = {
                    if (state.rows.isNotEmpty()) {
                        TextButton(onClick = viewModel::syncNow, enabled = state.isOnline && !state.isSyncing) {
                            Text(if (state.isSyncing) "Syncing…" else "Sync now")
                        }
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            !state.isLoaded -> Column(modifier) { LoadingState() }
            state.rows.isEmpty() -> EmptyState("Everything is synced.", modifier)
            else -> LazyColumn(modifier, contentPadding = PaddingValues(bottom = 24.dp)) {
                // Sync issues first: they never go on their own
                val (issues, waiting) = state.rows.partition { it.needsAttention }
                if (issues.isNotEmpty()) {
                    item { SectionTitle("Needs your attention") }
                    item { Hint("These weren't synced. Open one to decide what happens to it; the rest of your changes still sync.") }
                    items(issues, key = { it.seq }) { row ->
                        ChangeRow(row, onDiscard = { viewModel.askDiscard(row) }, onOpen = { onOpenIssue(row.seq) })
                        HorizontalDivider()
                    }
                }
                if (waiting.isNotEmpty()) {
                    if (issues.isNotEmpty()) item { SectionTitle("Waiting to sync") }
                    item {
                        Hint(if (state.isOnline) "These are sent in the order you made them." else "These are sent when you're back online, in the order you made them.")
                    }
                    items(waiting, key = { it.seq }) { row ->
                        ChangeRow(row, onDiscard = { viewModel.askDiscard(row) }, onOpen = null)
                        HorizontalDivider()
                    }
                }
            }
        }
    }

    state.confirming?.let { row ->
        AlertDialog(
            onDismissRequest = { viewModel.askDiscard(null) },
            title = { Text("Discard this change?") },
            text = {
                Text("\"${row.description}\" won't be synced. Other waiting changes of the same thing are discarded too, and it goes back to how it is on ReviewApp.")
            },
            confirmButton = { TextButton(onClick = viewModel::discard) { Text("Discard", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { viewModel.askDiscard(null) }) { Text("Keep") } },
        )
    }
}

/** A section's title. */
@Composable
private fun SectionTitle(text: String) = Text(
    text,
    style = MaterialTheme.typography.titleSmall,
    color = MaterialTheme.colorScheme.primary,
    modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 16.dp),
)

/** Explanatory text under a section's title. */
@Composable
private fun Hint(text: String) = Text(
    text,
    style = MaterialTheme.typography.bodyMedium,
    color = MaterialTheme.colorScheme.onSurfaceVariant,
    modifier = Modifier.padding(16.dp),
)

/**
 * One waiting change. [onOpen] is given for a change in Sync issues, which shows "Resolve" and opens its choices;
 * the others offer Discard (disabled while the change is being sent).
 */
@Composable
private fun ChangeRow(row: PendingChangeRow, onDiscard: () -> Unit, onOpen: (() -> Unit)?) {
    ListItem(
        leadingContent = {
            Icon(
                if (row.needsAttention) Icons.Outlined.WarningAmber else Icons.Outlined.CloudUpload,
                contentDescription = null,
                tint = if (row.needsAttention) AppTheme.extendedColors.warning else MaterialTheme.colorScheme.onSurfaceVariant,
            )
        },
        headlineContent = { Text(row.description) },
        supportingContent = {
            Text(
                row.status,
                color = if (row.needsAttention) AppTheme.extendedColors.warning else MaterialTheme.colorScheme.onSurfaceVariant,
            )
        },
        trailingContent = {
            Column(verticalArrangement = Arrangement.Center) {
                if (onOpen != null) {
                    TextButton(onClick = onOpen) { Text("Resolve") }
                } else {
                    TextButton(onClick = onDiscard, enabled = row.canDiscard) { Text("Discard") }
                }
            }
        },
        modifier = Modifier.fillMaxWidth().then(if (onOpen != null) Modifier.clickable(onClick = onOpen) else Modifier),
    )
}
