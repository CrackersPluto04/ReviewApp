package com.reviewapp.android.ui.offline

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.CloudOff
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.SdStorage
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.PremiumUpsell
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.formatBytes
import com.reviewapp.android.util.pluralize

/** Offline mode (Premium), opened from the account settings. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OfflineSettingsScreen(
    onBack: () -> Unit,
    onOpenPremium: () -> Unit,
    viewModel: OfflineSettingsViewModel = viewModel(factory = OfflineSettingsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    Scaffold(
        topBar = { TopAppBar(title = { Text("Offline mode") }, navigationIcon = { BackButton(onBack) }) },
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
                if (state.status.mode.usesLocalCopy) {
                    item { HorizontalDivider(Modifier.padding(top = 8.dp)) }
                    item { SectionHeader("Storage") }
                    item {
                        ListItem(
                            headlineContent = { Text("Offline data on this phone") },
                            supportingContent = { Text(formatBytes(state.storageBytes)) },
                            leadingContent = { Icon(Icons.Outlined.SdStorage, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
                        )
                    }
                }
            }
            item { Footnote() }
        }
    }

    if (state.confirmingDisable) {
        DisableDialog(state.status.pendingChanges, onConfirm = viewModel::confirmDisable, onDismiss = viewModel::dismissDisable)
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
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 16.dp, bottom = 4.dp),
    )
}

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
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Turn off offline mode?") },
        text = {
            Text(
                if (pendingChanges > 0) {
                    val (count, verb, pronoun) = if (pendingChanges == 1) Triple("1 change", "hasn't", "It'll") else Triple(pluralize(pendingChanges, "change"), "haven't", "They'll")
                    "$count made offline $verb synced yet. $pronoun still be sent when you're online, " +
                        "then the offline copy is removed from this phone."
                } else {
                    "The offline copy of your data is removed from this phone. You can turn offline mode back on any time."
                },
            )
        },
        confirmButton = { TextButton(onClick = onConfirm) { Text("Turn off") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
