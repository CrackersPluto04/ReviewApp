package com.reviewapp.android.ui.components

import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.ui.theme.AppTheme

/**
 * The offline mode marker of something changed on this phone: "Waiting to sync" until the backend accepted it,
 * "Needs attention" when it is in Sync issues, nothing once synced. [compact] shows the icon only.
 */
@Composable
fun SyncBadge(syncState: Int, modifier: Modifier = Modifier, compact: Boolean = false) {
    if (syncState == SyncState.SYNCED) return
    val issue = syncState == SyncState.ISSUE
    val label = if (issue) "Needs attention" else "Waiting to sync"
    val color = if (issue) AppTheme.extendedColors.warning else MaterialTheme.colorScheme.onSurfaceVariant
    Row(verticalAlignment = Alignment.CenterVertically, modifier = modifier.semantics(mergeDescendants = true) { contentDescription = label }) {
        Icon(
            if (issue) Icons.Outlined.WarningAmber else Icons.Outlined.CloudUpload,
            contentDescription = null,
            tint = color,
            modifier = Modifier.size(14.dp),
        )
        if (!compact) {
            Text(label, style = MaterialTheme.typography.labelSmall, color = color, modifier = Modifier.padding(start = 4.dp))
        }
    }
}
