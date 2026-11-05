package com.reviewapp.android.ui.offline

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.CloudDownload
import androidx.compose.material.icons.outlined.CloudOff
import androidx.compose.material.icons.outlined.CloudSync
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.DeleteSweep
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.SdStorage
import androidx.compose.material.icons.outlined.Storage
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material.icons.outlined.Wifi
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.data.local.CacheBudget
import com.reviewapp.android.data.sync.LibraryDownloadState
import com.reviewapp.android.data.sync.OfflineMode
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.PremiumUpsell
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.formatAgo
import com.reviewapp.android.util.formatBytes
import com.reviewapp.android.util.pluralize

/** Offline mode (Premium), opened from the account settings. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OfflineSettingsScreen(
    onBack: () -> Unit,
    onOpenPremium: () -> Unit,
    onOpenChanges: () -> Unit,
    viewModel: OfflineSettingsViewModel = viewModel(factory = OfflineSettingsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    LaunchedEffect(viewModel) { viewModel.messages.collect { snackbarHostState.showSnackbar(it) } }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Offline mode") }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        LazyColumn(Modifier.fillMaxSize().padding(padding), contentPadding = PaddingValues(bottom = 24.dp)) {
            item { Intro() }
            if (!state.entitled) {
                item {
                    PremiumUpsell(
                        onSeePremium = onOpenPremium,
                        title = "Offline mode is a Premium feature",
                        message = "Subscribe to Premium to use the app without a connection.",
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                    )
                }
            } else {
                item { OfflineSwitch(state.enabled, onToggle = viewModel::onToggle) }
                offlineStatusView(state.status, System.currentTimeMillis())?.let { view ->
                    item { StatusCard(view) }
                }
                val mode = state.status.mode
                if (state.status.pendingChanges > 0) {
                    item {
                        SettingRow(
                            Icons.Outlined.CloudUpload,
                            "Waiting changes",
                            listOfNotNull(
                                state.status.toSend.takeIf { it > 0 }?.let { "${pluralize(it, "change")} to sync" },
                                state.status.issues.takeIf { it > 0 }?.let { "$it need your attention" },
                            ).joinToString(" · "),
                            onClick = onOpenChanges,
                        )
                    }
                }
                if (mode is OfflineMode.Active || mode is OfflineMode.Preparing || mode is OfflineMode.ReadOnly) {
                    item { SyncRow(state, onSyncNow = viewModel::syncNow) }
                    item { Divider() }
                    item { SectionHeader("Before a trip") }
                    item { DownloadSection(state, viewModel) }
                }
                if (mode.usesLocalCopy) {
                    item { Divider() }
                    item { SectionHeader("Storage") }
                    item {
                        SettingRow(Icons.Outlined.SdStorage, "Offline data on this phone", formatBytes(state.storageBytes))
                    }
                    item {
                        SettingRow(
                            Icons.Outlined.Storage,
                            "Cache size",
                            "${state.budget.label} for pages and images you've opened. Your own content isn't counted.",
                            onClick = { viewModel.openDialog(OfflineDialog.Budget) },
                        )
                    }
                    item {
                        SettingRow(
                            Icons.Outlined.DeleteSweep,
                            "Clear cached data",
                            "Removes saved pages and images. Your reviews, collections and waiting changes stay.",
                            onClick = { viewModel.openDialog(OfflineDialog.ClearCache) },
                        )
                    }
                }
            }
            item { Footnote() }
        }
    }

    when (state.dialog) {
        OfflineDialog.Disable -> DisableDialog(state.status.pendingChanges, viewModel::confirmDisable, viewModel::dismissDialog)
        OfflineDialog.ClearCache -> ConfirmDialog(
            title = "Clear cached data?",
            text = "Saved pages and images of things you've opened are removed from this phone. Your own reviews, " +
                "collections and changes waiting to sync are kept.",
            confirm = "Clear",
            onConfirm = viewModel::clearCache,
            onDismiss = viewModel::dismissDialog,
        )
        OfflineDialog.Budget -> BudgetDialog(state.budget, viewModel::setBudget, viewModel::dismissDialog)
        null -> Unit
    }
}

@Composable
private fun Intro() {
    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.Top) {
        Icon(Icons.Outlined.CloudOff, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(end = 16.dp, top = 2.dp))
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("Use ReviewApp without a connection", style = MaterialTheme.typography.titleMedium)
            Text(
                "Your reviews, collections, profile, follows and notifications are kept on this phone. " +
                    "Changes you make offline are synced when you're back online.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun OfflineSwitch(enabled: Boolean, onToggle: (Boolean) -> Unit) {
    ListItem(
        headlineContent = { Text("Offline mode") },
        supportingContent = { Text(if (enabled) "On for this account on this phone" else "Off: the app needs a connection") },
        trailingContent = { Switch(checked = enabled, onCheckedChange = null) },
        modifier = Modifier.toggleable(value = enabled, role = Role.Switch, onValueChange = onToggle),
    )
}

@Composable
private fun StatusCard(view: OfflineStatusView) {
    val (icon, tint) = when (view.tone) {
        StatusTone.Good -> Icons.Outlined.CheckCircle to AppTheme.extendedColors.success
        StatusTone.Warning -> Icons.Outlined.WarningAmber to AppTheme.extendedColors.warning
        StatusTone.Neutral -> Icons.Outlined.Info to MaterialTheme.colorScheme.primary
    }
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
    ) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.Top) {
            Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.padding(end = 12.dp, top = 2.dp).size(22.dp))
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(view.title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Text(view.message, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

@Composable
private fun SyncRow(state: OfflineSettingsUiState, onSyncNow: () -> Unit) {
    ListItem(
        leadingContent = { Icon(Icons.Outlined.CloudSync, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
        headlineContent = { Text("Sync") },
        supportingContent = {
            Text(
                // The status card above says when it last synced
                when {
                    state.isSyncing -> "Syncing…"
                    !state.isOnline -> "Syncs when you're back online"
                    else -> "Syncs on its own; sync now to get changes from the web at once"
                },
            )
        },
        trailingContent = {
            if (state.isSyncing) {
                CircularProgressIndicator(Modifier.size(24.dp))
            } else {
                TextButton(onClick = onSyncNow, enabled = state.isOnline) { Text("Sync now") }
            }
        },
    )
}

@Composable
private fun DownloadSection(state: OfflineSettingsUiState, viewModel: OfflineSettingsViewModel) {
    Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.Top) {
            Icon(Icons.Outlined.CloudDownload, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(end = 16.dp, top = 2.dp))
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("Make my library available offline", style = MaterialTheme.typography.bodyLarge)
                Text(
                    "Saves the details and cover art of everything in your reviews and collections.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        when (val download = state.download) {
            is LibraryDownloadState.Running -> {
                if (download.total > 0) {
                    LinearProgressIndicator(progress = { download.done.toFloat() / download.total }, modifier = Modifier.fillMaxWidth())
                    Text("${download.done} of ${pluralize(download.total, "title")}", style = MaterialTheme.typography.bodySmall)
                } else {
                    LinearProgressIndicator(Modifier.fillMaxWidth())
                    Text("Refreshing your library…", style = MaterialTheme.typography.bodySmall)
                }
                OutlinedButton(onClick = viewModel::cancelDownload) { Text("Stop") }
            }
            LibraryDownloadState.Waiting -> {
                Text(
                    if (state.wifiOnly) "Waiting for Wi-Fi…" else "Waiting for a connection…",
                    style = MaterialTheme.typography.bodySmall,
                )
                OutlinedButton(onClick = viewModel::cancelDownload) { Text("Cancel") }
            }
            LibraryDownloadState.Idle -> {
                state.status.libraryDownloadedAt?.let {
                    Text("Last done ${formatAgo(it, System.currentTimeMillis())}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Button(onClick = viewModel::startDownload) { Text("Download") }
            }
        }
    }
    ListItem(
        leadingContent = { Icon(Icons.Outlined.Wifi, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
        headlineContent = { Text("Download on Wi-Fi only") },
        trailingContent = { Switch(checked = state.wifiOnly, onCheckedChange = null) },
        modifier = Modifier.toggleable(value = state.wifiOnly, role = Role.Switch, onValueChange = viewModel::setWifiOnly),
    )
}

@Composable
private fun SettingRow(icon: ImageVector, title: String, subtitle: String, onClick: (() -> Unit)? = null) {
    ListItem(
        headlineContent = { Text(title) },
        supportingContent = { Text(subtitle) },
        leadingContent = { Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
        modifier = if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier,
    )
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 16.dp, bottom = 4.dp),
    )
}

@Composable
private fun Divider() = HorizontalDivider(Modifier.padding(top = 8.dp))

@Composable
private fun Footnote() {
    Text(
        "Offline mode works in the Android app only. Searching new titles, camera recognition, replies, " +
            "subscription and account changes always need a connection.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 16.dp),
    )
}

@Composable
private fun DisableDialog(pendingChanges: Int, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    ConfirmDialog(
        title = "Turn off offline mode?",
        text = if (pendingChanges > 0) {
            val (count, verb, pronoun) = if (pendingChanges == 1) Triple("1 change", "hasn't", "It'll") else Triple(pluralize(pendingChanges, "change"), "haven't", "They'll")
            "$count made offline $verb synced yet. $pronoun still be sent when you're online, " +
                "then the offline copy is removed from this phone."
        } else {
            "The offline copy of your data is removed from this phone. You can turn offline mode back on any time."
        },
        confirm = "Turn off",
        onConfirm = onConfirm,
        onDismiss = onDismiss,
    )
}

@Composable
private fun ConfirmDialog(title: String, text: String, confirm: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = { Text(text) },
        confirmButton = { TextButton(onClick = onConfirm) { Text(confirm) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

@Composable
private fun BudgetDialog(current: CacheBudget, onSelect: (CacheBudget) -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Cache size") },
        text = {
            Column {
                Text(
                    "Space for pages and images you've opened. The oldest go first when it's full.",
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.padding(bottom = 8.dp),
                )
                CacheBudget.entries.forEach { budget ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .selectable(selected = budget == current, role = Role.RadioButton, onClick = { onSelect(budget) })
                            .padding(vertical = 4.dp),
                    ) {
                        RadioButton(selected = budget == current, onClick = null)
                        Text(budget.label, modifier = Modifier.padding(start = 12.dp))
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Close") } },
    )
}
