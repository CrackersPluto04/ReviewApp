package com.reviewapp.android.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Card
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.collection.MediaCollectionButtons
import com.reviewapp.android.util.formatDate

/**
 * Search/discover result: poster, title, release date, overview (artists for music) and the
 * average score. [onNeedStats] is called once when the card first appears so the score loads lazily.
 * Logged-in users also get the add-to-collection and favourite buttons ([MediaCollectionButtons]).
 */
@Composable
fun MediaCard(
    media: Media,
    stats: ScoreStats?,
    onNeedStats: (Media) -> Unit,
    onClick: (Media) -> Unit,
    modifier: Modifier = Modifier,
) {
    LaunchedEffect(media.key) { onNeedStats(media) }

    Card(onClick = { onClick(media) }, modifier = modifier.fillMaxWidth()) {
        Row(Modifier.height(IntrinsicSize.Min).heightIn(min = 150.dp)) {
            Poster(
                url = media.posterUrl,
                mediaType = media.mediaType,
                cornerRadius = 0.dp,
                modifier = Modifier
                    .width(100.dp)
                    .fillMaxHeight(),
            )
            Column(Modifier.weight(1f)) {
                Column(Modifier.weight(1f).padding(12.dp)) {
                    Text(
                        media.title,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        formatDate(media.releaseDate) ?: "Unknown year",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Spacer(Modifier.height(6.dp))
                    val description = if (media.mediaType == MediaType.Music) media.creator else media.overview
                    if (description.isNotBlank()) {
                        Text(
                            description,
                            style = MaterialTheme.typography.bodySmall,
                            maxLines = 3,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                HorizontalDivider()
                Row(Modifier.heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically) {
                    ScoreStatsText(stats, Modifier.weight(1f).padding(horizontal = 12.dp, vertical = 8.dp))
                    MediaCollectionButtons(media)
                }
            }
        }
    }
}

/** Poster with its title, used in the home page carousels. */
@Composable
fun PosterTile(media: Media, onClick: (Media) -> Unit, modifier: Modifier = Modifier) {
    Column(
        modifier
            .width(120.dp)
            .clickable { onClick(media) },
    ) {
        Poster(media.posterUrl, media.mediaType, Modifier.size(width = 120.dp, height = 180.dp))
        Text(
            media.title,
            style = MaterialTheme.typography.bodyMedium,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
        )
    }
}
