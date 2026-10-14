package com.reviewapp.android.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Comment
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.MediaReview
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.ui.theme.color
import com.reviewapp.android.util.formatDate
import com.reviewapp.android.util.formatScore
import com.reviewapp.android.util.isDifferentDay
import com.reviewapp.android.util.pluralize
import com.reviewapp.android.util.scoreTier

/** "8/10" with "Great" / "Mixed" / "Poor" under it, coloured by the score. */
@Composable
fun ScoreBadge(score: Double, modifier: Modifier = Modifier) {
    val tier = scoreTier(score)
    Column(modifier, horizontalAlignment = Alignment.End) {
        Text(
            "${formatScore(score)}/10",
            style = MaterialTheme.typography.titleLarge,
            fontWeight = FontWeight.Bold,
            color = tier.color(),
        )
        Text(tier.label, style = MaterialTheme.typography.labelMedium, color = tier.color())
    }
}

/** A review on a media's page (the web app's `ReviewCard` in `media` mode, without replies for now). */
@Composable
fun ReviewCard(review: MediaReview, onOpenProfile: (String) -> Unit, modifier: Modifier = Modifier) {
    Card(modifier.fillMaxWidth()) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Row(
                Modifier
                    .weight(1f)
                    .clip(MaterialTheme.shapes.small)
                    .clickable { onOpenProfile(review.username) },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                UserAvatar(review.username, review.profilePictureUrl)
                Spacer(Modifier.width(12.dp))
                Column {
                    Text(review.username, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                    ReviewDates(review.createdAt, review.updatedAt)
                }
            }
            ScoreBadge(review.score)
        }
        HorizontalDivider()
        ReviewBody(review.reviewText, review.pros, review.cons, review.replyCount)
    }
}

/** A review in a profile: the reviewed media instead of the author, plus owner-only visibility and actions. */
@Composable
fun ProfileReviewCard(
    review: ProfileReview,
    onOpenMedia: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Card(onClick = onOpenMedia, modifier = modifier.fillMaxWidth()) {
        Row(Modifier.padding(12.dp)) {
            Poster(review.posterUrl, review.mediaType, Modifier.size(width = 60.dp, height = 90.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    review.mediaTitle,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
                ReviewDates(review.createdAt, review.updatedAt)
                if (review.isOwner) {
                    Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            review.visibility.icon,
                            contentDescription = null,
                            modifier = Modifier.size(14.dp),
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Spacer(Modifier.width(4.dp))
                        Text(
                            review.visibility.label,
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
            Spacer(Modifier.width(8.dp))
            Column(horizontalAlignment = Alignment.End) {
                if (review.isOwner) ReviewActionsMenu(onEdit, onDelete)
                ScoreBadge(review.score)
            }
        }
        HorizontalDivider()
        ReviewBody(review.reviewText, review.pros, review.cons, review.replyCount)
    }
}

@Composable
private fun ReviewActionsMenu(onEdit: () -> Unit, onDelete: () -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { expanded = true }, modifier = Modifier.size(32.dp)) {
            Icon(Icons.Filled.MoreVert, contentDescription = "Review actions")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = { Text("Edit") },
                leadingIcon = { Icon(Icons.Outlined.Edit, null) },
                onClick = {
                    expanded = false
                    onEdit()
                },
            )
            DropdownMenuItem(
                text = { Text("Delete", color = MaterialTheme.colorScheme.error) },
                leadingIcon = { Icon(Icons.Outlined.Delete, null, tint = MaterialTheme.colorScheme.error) },
                onClick = {
                    expanded = false
                    onDelete()
                },
            )
        }
    }
}

@Composable
private fun ReviewDates(createdAt: String, updatedAt: String) {
    Text(
        "Created ${formatDate(createdAt) ?: "-"}",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    if (isDifferentDay(createdAt, updatedAt)) {
        Text(
            "Updated ${formatDate(updatedAt) ?: "-"}",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

/** Text, pros/cons boxes and the reply count, shared by the review cards. */
@Composable
private fun ReviewBody(reviewText: String?, pros: String?, cons: String?, replyCount: Int) {
    Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        if (reviewText.isNullOrBlank()) {
            Text(
                "No written review.",
                style = MaterialTheme.typography.bodyMedium,
                fontStyle = FontStyle.Italic,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            Text(reviewText, style = MaterialTheme.typography.bodyMedium)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ProsConsBox(
                title = "Pros",
                text = pros,
                titleColor = AppTheme.extendedColors.success,
                container = AppTheme.extendedColors.successContainer,
                modifier = Modifier.weight(1f),
            )
            ProsConsBox(
                title = "Cons",
                text = cons,
                titleColor = MaterialTheme.colorScheme.error,
                container = AppTheme.extendedColors.errorContainer,
                modifier = Modifier.weight(1f),
            )
        }
        if (replyCount > 0) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.AutoMirrored.Outlined.Comment,
                    contentDescription = null,
                    modifier = Modifier.size(16.dp),
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(Modifier.width(4.dp))
                Text(
                    pluralize(replyCount, "reply", "replies"),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun ProsConsBox(title: String, text: String?, titleColor: Color, container: Color, modifier: Modifier) {
    Surface(color = container, shape = MaterialTheme.shapes.medium, modifier = modifier) {
        Column(Modifier.padding(10.dp)) {
            Text(title, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, color = titleColor)
            Text(
                if (text.isNullOrBlank()) "-" else text,
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

/** Home feed card: who reviewed what, the score and a snippet. Tapping the author opens their profile. */
@Composable
fun FeedReviewCard(
    review: FeedReview,
    onClick: () -> Unit,
    onOpenProfile: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    Card(onClick = onClick, modifier = modifier.fillMaxWidth()) {
        Row(Modifier.padding(12.dp)) {
            Poster(review.posterUrl, review.mediaType, Modifier.size(width = 60.dp, height = 90.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    review.mediaTitle,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Row(
                    Modifier
                        .clip(MaterialTheme.shapes.small)
                        .clickable { onOpenProfile(review.username) }
                        .padding(vertical = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    UserAvatar(review.username, review.profilePictureUrl, size = 20.dp)
                    Spacer(Modifier.width(6.dp))
                    Text(
                        "${review.username} · ${formatDate(review.createdAt) ?: ""}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                val snippet = review.reviewText?.takeIf { it.isNotBlank() }
                    ?: listOfNotNull(
                        review.pros?.takeIf { it.isNotBlank() }?.let { "Pros: $it" },
                        review.cons?.takeIf { it.isNotBlank() }?.let { "Cons: $it" },
                    ).joinToString("  ")
                Text(
                    snippet,
                    style = MaterialTheme.typography.bodySmall,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
            Spacer(Modifier.width(8.dp))
            ScoreBadge(review.score)
        }
    }
}
