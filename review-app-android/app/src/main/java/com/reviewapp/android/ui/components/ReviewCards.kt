package com.reviewapp.android.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Comment
import androidx.compose.material3.Card
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.reviewapp.android.domain.model.FeedReview
import com.reviewapp.android.domain.model.MediaReview
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
fun ReviewCard(review: MediaReview, modifier: Modifier = Modifier) {
    Card(modifier.fillMaxWidth()) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            UserAvatar(review.username, review.profilePictureUrl)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(review.username, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Text(
                    "Created ${formatDate(review.createdAt) ?: "-"}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                if (isDifferentDay(review.createdAt, review.updatedAt)) {
                    Text(
                        "Updated ${formatDate(review.updatedAt) ?: "-"}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            ScoreBadge(review.score)
        }
        HorizontalDivider()
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            if (review.reviewText.isNullOrBlank()) {
                Text(
                    "No written review.",
                    style = MaterialTheme.typography.bodyMedium,
                    fontStyle = FontStyle.Italic,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                Text(review.reviewText, style = MaterialTheme.typography.bodyMedium)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ProsConsBox(
                    title = "Pros",
                    text = review.pros,
                    titleColor = AppTheme.extendedColors.success,
                    container = AppTheme.extendedColors.successContainer,
                    modifier = Modifier.weight(1f),
                )
                ProsConsBox(
                    title = "Cons",
                    text = review.cons,
                    titleColor = MaterialTheme.colorScheme.error,
                    container = AppTheme.extendedColors.errorContainer,
                    modifier = Modifier.weight(1f),
                )
            }
            if (review.replyCount > 0) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.AutoMirrored.Outlined.Comment,
                        contentDescription = null,
                        modifier = Modifier.size(16.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Spacer(Modifier.width(4.dp))
                    Text(
                        pluralize(review.replyCount, "reply", "replies"),
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
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

/** Home feed card: who reviewed what, the score and a snippet. */
@Composable
fun FeedReviewCard(review: FeedReview, onClick: () -> Unit, modifier: Modifier = Modifier) {
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
                Row(verticalAlignment = Alignment.CenterVertically) {
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
