package com.reviewapp.android.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.WorkspacePremium
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** The gold of the Premium badge, the same as the gold achievement tier (and the web's PREMIUM_COLOR). */
val PremiumGold = Color(0xFFD4AF37)

/** The public badge of subscribers (profile header, subscription card). */
@Composable
fun PremiumBadge(modifier: Modifier = Modifier) {
    Surface(
        color = Color.Transparent,
        contentColor = PremiumGold,
        border = BorderStroke(1.dp, PremiumGold),
        shape = MaterialTheme.shapes.small,
        modifier = modifier.semantics(mergeDescendants = true) { contentDescription = "Premium member" },
    ) {
        Row(Modifier.padding(horizontal = 6.dp, vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.WorkspacePremium, contentDescription = null, modifier = Modifier.size(16.dp))
            Text(
                "Premium",
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(start = 4.dp),
            )
        }
    }
}

/**
 * Inline prompt for a locked Premium feature: show it instead of hiding the feature (the web's
 * `PremiumUpsell`). Future Premium features (offline mode, camera recognition) use it too.
 */
@Composable
fun PremiumUpsell(
    onSeePremium: () -> Unit,
    modifier: Modifier = Modifier,
    title: String = "This is a Premium feature",
    message: String = "Subscribe to Premium to unlock it, along with every other Premium feature.",
) {
    Card(
        modifier = modifier.fillMaxWidth(),
        border = BorderStroke(1.dp, PremiumGold),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
    ) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.WorkspacePremium, contentDescription = null, tint = PremiumGold, modifier = Modifier.size(36.dp))
            Column(Modifier.weight(1f).padding(start = 12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                Text(message, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Button(onClick = onSeePremium, modifier = Modifier.align(Alignment.End).padding(end = 16.dp, bottom = 12.dp)) {
            Text("See Premium")
        }
    }
}
