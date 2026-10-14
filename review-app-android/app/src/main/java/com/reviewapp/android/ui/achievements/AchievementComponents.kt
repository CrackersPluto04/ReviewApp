package com.reviewapp.android.ui.achievements

import androidx.annotation.DrawableRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.background
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.EmojiEvents
import androidx.compose.material.icons.filled.MilitaryTech
import androidx.compose.material.icons.outlined.MilitaryTech
import androidx.compose.material3.Card
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.ColorMatrix
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.reviewapp.android.R
import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.AchievementCategory
import com.reviewapp.android.domain.model.AchievementTier
import com.reviewapp.android.ui.components.DropdownFilterChip
import com.reviewapp.android.ui.components.FilterChipRow
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.AchievementFilter
import com.reviewapp.android.util.AchievementSort
import com.reviewapp.android.util.AchievementStatus
import com.reviewapp.android.util.Option

/** The web's tier colours (`src/utils/achievements.ts`). */
val AchievementTier.color: Color
    get() = when (this) {
        AchievementTier.Bronze -> Color(0xFFCD7F32)
        AchievementTier.Silver -> Color(0xFF7A90AE)
        AchievementTier.Gold -> Color(0xFFD4AF37)
    }

/** The vector versions of `review-app/public/achievements/<iconKey>.svg`; null for an unknown key. */
@DrawableRes
fun achievementIcon(iconKey: String): Int? = when (iconKey) {
    "movie-master" -> R.drawable.achievement_movie_master
    "binge-watcher" -> R.drawable.achievement_binge_watcher
    "music-lover" -> R.drawable.achievement_music_lover
    "critic" -> R.drawable.achievement_critic
    "conversation-starter" -> R.drawable.achievement_conversation_starter
    "professional-hater" -> R.drawable.achievement_professional_hater
    "curator" -> R.drawable.achievement_curator
    else -> null
}

private val sortOptions = AchievementSort.entries.map { Option(it.name, it.label) }

private val greyscale = ColorFilter.colorMatrix(ColorMatrix().apply { setToSaturation(0f) })

/** Status chips, sort and Reset, then the category chips (the web's `AchievementFilterBar`). */
@Composable
fun AchievementFilterChips(filter: AchievementFilter, onFilterChange: (AchievementFilter) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
            AchievementStatus.entries.forEach { status ->
                FilterChip(
                    selected = status in filter.statuses,
                    onClick = { onFilterChange(filter.copy(statuses = filter.statuses.toggled(status))) },
                    label = { Text(status.label) },
                )
            }
            DropdownFilterChip(
                label = "Sort",
                options = sortOptions,
                selected = filter.sort.name,
                onSelect = { name ->
                    onFilterChange(filter.copy(sort = AchievementSort.entries.firstOrNull { it.name == name } ?: AchievementSort.Default))
                },
                highlighted = filter.sort != AchievementSort.Default,
            )
            if (!filter.isDefault) {
                TextButton(onClick = { onFilterChange(AchievementFilter()) }) { Text("Reset") }
            }
        }
        FilterChipRow(Modifier.padding(horizontal = 16.dp)) {
            AchievementCategory.entries.forEach { category ->
                FilterChip(
                    selected = category in filter.categories,
                    onClick = { onFilterChange(filter.copy(categories = filter.categories.toggled(category))) },
                    label = { Text(category.name) },
                )
            }
        }
    }
}

private fun <T> Set<T>.toggled(value: T): Set<T> = if (value in this) this - value else this + value

/**
 * One achievement as in the wireframe: the icon and the tier medals on the left, the next tier's
 * goal and the progress towards it on the right. Greyed out until the first tier is earned.
 */
@Composable
fun AchievementRow(achievement: Achievement, modifier: Modifier = Modifier) {
    val locked = achievement.unlockedCount == 0
    Card(modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                AchievementIcon(achievement.iconKey, locked, Modifier.size(64.dp))
                TierMedals(achievement)
            }
            Spacer(Modifier.width(16.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(
                    achievement.title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
                // The goal of the next tier, or of the last one once everything is earned
                val shownTier = achievement.nextTier ?: achievement.tiers.lastOrNull()
                if (shownTier != null) {
                    Text(
                        shownTier.description,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                val next = achievement.nextTier
                if (next == null) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            Icons.Filled.CheckCircle,
                            contentDescription = null,
                            tint = AppTheme.extendedColors.success,
                            modifier = Modifier.size(18.dp),
                        )
                        Spacer(Modifier.width(4.dp))
                        Text(
                            "Completed",
                            style = MaterialTheme.typography.labelLarge,
                            color = AppTheme.extendedColors.success,
                        )
                    }
                } else {
                    val shown = achievement.currentProgress.coerceAtMost(next.targetValue)
                    Text(
                        "Next tier (${next.tier.name}): $shown/${next.targetValue}",
                        style = MaterialTheme.typography.labelLarge,
                    )
                    LinearProgressIndicator(
                        progress = { if (next.targetValue > 0) shown.toFloat() / next.targetValue else 0f },
                        color = next.tier.color,
                        trackColor = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.12f),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                Text(
                    "Unlocked ${achievement.unlockedCount}/${achievement.tiers.size}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

/** The card's icon, greyscale and faded while locked; a trophy when the key has no drawable. */
@Composable
fun AchievementIcon(iconKey: String, locked: Boolean, modifier: Modifier = Modifier) {
    val alpha = if (locked) 0.55f else 1f
    val drawable = achievementIcon(iconKey)
    if (drawable != null) {
        Image(
            painterResource(drawable),
            contentDescription = null,
            colorFilter = if (locked) greyscale else null,
            alpha = alpha,
            modifier = modifier,
        )
    } else {
        Box(
            modifier.clip(CircleShape).background(MaterialTheme.colorScheme.secondaryContainer).alpha(alpha),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                Icons.Filled.EmojiEvents,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSecondaryContainer,
                modifier = Modifier.fillMaxWidth().padding(12.dp),
            )
        }
    }
}

@Composable
private fun TierMedals(achievement: Achievement) {
    Row {
        achievement.tiers.forEach { tier ->
            Icon(
                // Outlined while not earned, so a locked medal never looks like a grey (Silver) one
                if (tier.isUnlocked) Icons.Filled.MilitaryTech else Icons.Outlined.MilitaryTech,
                contentDescription = "${tier.tier.name} ${if (tier.isUnlocked) "earned" else "not earned"}",
                tint = if (tier.isUnlocked) tier.tier.color else MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f),
                modifier = Modifier.size(22.dp),
            )
        }
    }
}
