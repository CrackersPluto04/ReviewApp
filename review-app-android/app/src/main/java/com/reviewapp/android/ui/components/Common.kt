package com.reviewapp.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ImageNotSupported
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.RateReview
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.theme.color
import com.reviewapp.android.util.formatScore
import com.reviewapp.android.util.pluralize
import com.reviewapp.android.util.scoreTier
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter

@Composable
fun AppLogo(modifier: Modifier = Modifier, large: Boolean = false) {
    Row(modifier = modifier, verticalAlignment = Alignment.CenterVertically) {
        Icon(
            Icons.Filled.RateReview,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size(if (large) 40.dp else 28.dp),
        )
        Spacer(Modifier.width(8.dp))
        Text(
            "THE Review App",
            style = if (large) MaterialTheme.typography.headlineSmall else MaterialTheme.typography.titleLarge,
            fontWeight = FontWeight.Bold,
        )
    }
}

@Composable
fun BackButton(onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
    }
}

/**
 * Poster with a neutral placeholder while loading and when there is none. The image is drawn over
 * the placeholder instead of using Coil's subcompose slots, so it also works inside intrinsic
 * measurements (SubcomposeLayout doesn't support them).
 */
@Composable
fun Poster(
    url: String?,
    mediaType: MediaType,
    modifier: Modifier = Modifier,
    cornerRadius: Dp = 8.dp,
) {
    Box(
        modifier
            .clip(RoundedCornerShape(cornerRadius))
            .background(MaterialTheme.colorScheme.surfaceVariant),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            if (mediaType == MediaType.Music) Icons.Filled.MusicNote else Icons.Filled.ImageNotSupported,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (!url.isNullOrBlank()) {
            AsyncImage(
                model = url,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.matchParentSize(),
            )
        }
    }
}

@Composable
fun UserAvatar(username: String, pictureUrl: String?, size: Dp = 40.dp, modifier: Modifier = Modifier) {
    Box(
        modifier
            .size(size)
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.secondaryContainer),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            username.firstOrNull()?.uppercase() ?: "?",
            color = MaterialTheme.colorScheme.onSecondaryContainer,
            fontWeight = FontWeight.Bold,
            fontSize = (size.value * 0.4f).sp,
        )
        if (!pictureUrl.isNullOrBlank()) {
            AsyncImage(
                model = pictureUrl,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.matchParentSize(),
            )
        }
    }
}

/** "8.5 / 10 • 12 reviews", or "-- / 10 • 0 reviews" before anyone reviewed it. */
@Composable
fun ScoreStatsText(stats: ScoreStats?, modifier: Modifier = Modifier) {
    val average = stats?.takeIf { it.reviewCount > 0 }?.averageScore
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Text(
            text = if (average != null) "${formatScore(average)} / 10" else "-- / 10",
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.Bold,
            color = if (average != null) scoreTier(average).color() else MaterialTheme.colorScheme.onSurface,
        )
        Text(
            text = " • " + if (stats == null) "…" else pluralize(stats.reviewCount, "review"),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

/** Calls [onLoadMore] when the list is scrolled close to its end. */
@Composable
fun InfiniteListEffect(listState: LazyListState, buffer: Int = 4, onLoadMore: () -> Unit) {
    val currentOnLoadMore = rememberUpdatedState(onLoadMore)
    LaunchedEffect(listState) {
        // The pair changes when items are added or the footer appears/disappears, so a page that
        // brought nothing new still triggers the next one while the user stays at the bottom
        snapshotFlow {
            val info = listState.layoutInfo
            (info.visibleItemsInfo.lastOrNull()?.index ?: -1) to info.totalItemsCount
        }
            .distinctUntilChanged()
            .filter { (lastVisible, total) -> total > 0 && lastVisible >= total - 1 - buffer }
            .collect { currentOnLoadMore.value() }
    }
}

/** Footer of an infinite list: a spinner while loading, an error with retry when a page failed. */
@Composable
fun ListFooter(isLoading: Boolean, error: String?, onRetry: () -> Unit) {
    when {
        isLoading -> Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) {
            CircularProgressIndicator(Modifier.size(28.dp))
        }
        error != null -> ErrorState(error, onRetry, Modifier.fillMaxWidth().padding(16.dp))
    }
}

@Composable
fun LoadingState(modifier: Modifier = Modifier) {
    Box(modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
        CircularProgressIndicator()
    }
}

@Composable
fun ErrorState(message: String, onRetry: (() -> Unit)?, modifier: Modifier = Modifier) {
    Column(
        modifier.padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(
            message,
            color = MaterialTheme.colorScheme.error,
            textAlign = TextAlign.Center,
            style = MaterialTheme.typography.bodyMedium,
        )
        if (onRetry != null) OutlinedButton(onClick = onRetry) { Text("Try again") }
    }
}

@Composable
fun EmptyState(message: String, modifier: Modifier = Modifier) {
    Text(
        message,
        modifier = modifier.fillMaxWidth().padding(32.dp),
        textAlign = TextAlign.Center,
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(
        text,
        style = MaterialTheme.typography.titleMedium,
        fontWeight = FontWeight.SemiBold,
        modifier = modifier,
    )
}

@Composable
fun VerticalSpace(height: Dp) = Spacer(Modifier.height(height))
