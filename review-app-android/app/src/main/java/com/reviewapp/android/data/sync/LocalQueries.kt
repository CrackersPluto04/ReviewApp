package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.LocalCollection
import com.reviewapp.android.data.local.offline.LocalReview
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.NotificationEntity
import com.reviewapp.android.data.repository.ReviewFilter
import com.reviewapp.android.domain.model.AppNotification
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaCollection
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.NotificationPage
import com.reviewapp.android.domain.model.NotificationSort
import com.reviewapp.android.domain.model.NotificationStatus
import com.reviewapp.android.domain.model.NotificationType
import com.reviewapp.android.domain.model.Page
import com.reviewapp.android.domain.model.ProfileReview
import com.reviewapp.android.domain.model.Visibility
import java.text.Normalizer
import java.time.Instant

/*
 * The offline copy answers like the API: the same filters, sorts and pages, so a screen can't tell (pure,
 * unit-tested against the server's rules in ReviewService / CollectionService / NotificationService).
 */

/** Sortable form of the API's ISO instants (their fractions have varying lengths, so strings don't sort). */
internal fun instantOf(raw: String): Instant = try {
    Instant.parse(raw)
} catch (_: Exception) {
    try {
        Instant.parse(raw.take(10) + "T00:00:00Z")
    } catch (_: Exception) {
        Instant.EPOCH
    }
}

object LocalReviewQuery {
    /** The API's page size of `User/{username}/reviews`. */
    const val PAGE_SIZE = 10

    fun page(reviews: List<LocalReview>, filter: ReviewFilter, page: Int, pageSize: Int = PAGE_SIZE): Page<ProfileReview> {
        val matching = reviews.filter { local ->
            val r = local.review
            r.score >= filter.minScore && r.score <= filter.maxScore &&
                (!filter.writtenOnly || listOf(r.reviewText, r.pros, r.cons).any { !it.isNullOrBlank() })
        }
        val sorted = when (filter.sortBy) {
            "created_asc" -> matching.sortedWith(compareBy<LocalReview> { instantOf(it.review.createdAt) }.thenBy { it.review.id })
            "updated_desc" -> matching.sortedWith(compareByDescending<LocalReview> { instantOf(it.review.updatedAt) }.thenByDescending { it.review.id })
            "updated_asc" -> matching.sortedWith(compareBy<LocalReview> { instantOf(it.review.updatedAt) }.thenBy { it.review.id })
            "score_desc" -> matching.sortedWith(compareByDescending<LocalReview> { it.review.score }.thenByDescending { it.review.id })
            "score_asc" -> matching.sortedWith(compareBy<LocalReview> { it.review.score }.thenBy { it.review.id })
            else -> matching.sortedWith(compareByDescending<LocalReview> { instantOf(it.review.createdAt) }.thenByDescending { it.review.id })
        }
        val totalPages = (sorted.size + pageSize - 1) / pageSize
        val items = sorted.drop((page - 1).coerceAtLeast(0) * pageSize).take(pageSize).map { it.toProfileReview() }
        return Page(items, page, totalPages, sorted.size)
    }

    fun LocalReview.toProfileReview() = ProfileReview(
        id = review.id,
        mediaTitle = title.orEmpty(),
        posterUrl = posterUrl,
        mediaType = MediaType.fromId(review.mediaType),
        externalApiId = review.externalApiId,
        score = review.score,
        reviewText = review.reviewText,
        pros = review.pros,
        cons = review.cons,
        // The API sends these as dates
        createdAt = review.createdAt.take(10),
        updatedAt = review.updatedAt.take(10),
        visibility = Visibility.fromId(review.visibility),
        isOwner = true,
        replyCount = review.replyCount,
        syncState = review.syncState,
    )
}

object LocalCollectionQuery {
    fun sorted(collections: List<LocalCollection>, sortBy: String): List<MediaCollection> {
        val byName = compareBy<LocalCollection, String>(String.CASE_INSENSITIVE_ORDER) { it.collection.name }
        val sorted = when (sortBy) {
            "createdAt_desc" -> collections.sortedWith(compareByDescending<LocalCollection> { instantOf(it.collection.createdAt) }.thenByDescending { it.collection.id })
            "name_asc" -> collections.sortedWith(byName)
            "name_desc" -> collections.sortedWith(byName.reversed())
            else -> collections.sortedWith(compareBy<LocalCollection> { instantOf(it.collection.createdAt) }.thenBy { it.collection.id })
        }
        return sorted.map { it.toMediaCollection() }
    }

    fun LocalCollection.toMediaCollection() = MediaCollection(
        id = collection.id,
        name = collection.name,
        visibility = Visibility.fromId(collection.visibility),
        createdAt = collection.createdAt.take(10),
        mediaCount = mediaCount,
        isOwner = true,
        isDefault = collection.isDefault,
        syncState = collection.syncState,
    )
}

object LocalNotificationQuery {
    /** Keyset paging like the API: [afterId] is the last notification of the previous page. */
    fun page(
        notifications: List<NotificationEntity>,
        status: NotificationStatus,
        sort: NotificationSort,
        afterId: Int?,
        pageSize: Int,
    ): NotificationPage {
        val visible = notifications.filterNot { it.deleted }
        val filtered = visible.filter {
            when (status) {
                NotificationStatus.All -> true
                NotificationStatus.Seen -> it.isSeen
                NotificationStatus.Unseen -> !it.isSeen
            }
        }
        val newestFirst = compareByDescending<NotificationEntity> { instantOf(it.createdAt) }.thenByDescending { it.id }
        val sorted = filtered.sortedWith(if (sort == NotificationSort.Oldest) newestFirst.reversed() else newestFirst)
        val start = afterId?.let { id -> sorted.indexOfFirst { it.id == id }.takeIf { it >= 0 }?.plus(1) ?: sorted.size } ?: 0
        val items = sorted.drop(start).take(pageSize)
        return NotificationPage(
            items = items.map { it.toDomain() },
            hasMore = start + items.size < sorted.size,
            totalCount = sorted.size,
            unseenCount = visible.count { !it.isSeen },
        )
    }

    fun NotificationEntity.toDomain() = AppNotification(
        id = id,
        type = NotificationType.fromName(type),
        title = title,
        message = message,
        isSeen = isSeen,
        createdAt = createdAt,
        actorUsername = actorUsername?.takeIf { it.isNotBlank() },
        actorProfilePictureUrl = actorProfilePictureUrl?.takeIf { it.isNotBlank() },
        reviewId = reviewId,
        replyId = replyId,
        groupCode = groupCode,
        syncState = syncState,
    )
}

/** Search of the offline library (own content and opened media) while there is no connection. */
object OfflineSearch {
    const val MAX_RESULTS = 50

    fun search(media: List<MediaEntity>, query: String, type: MediaType? = null): List<Media> {
        val words = normalize(query).split(' ').filter { it.isNotBlank() }
        if (words.isEmpty()) return emptyList()
        val whole = words.joinToString(" ")
        return media
            .asSequence()
            .filter { type == null || it.mediaType == type.id }
            .mapNotNull { entity ->
                val title = normalize(entity.title.orEmpty())
                val creator = normalize(entity.creator.orEmpty())
                if (!words.all { title.contains(it) || creator.contains(it) }) return@mapNotNull null
                val rank = when {
                    title.startsWith(whole) -> 0
                    title.split(' ').any { it.startsWith(words.first()) } -> 1
                    words.all { title.contains(it) } -> 2
                    else -> 3
                }
                Triple(entity, rank, entity.lastUsedAt)
            }
            .sortedWith(compareBy<Triple<MediaEntity, Int, Long>> { it.second }.thenByDescending { it.third })
            .take(MAX_RESULTS)
            .map { it.first.toMedia() }
            .toList()
    }

    /** Lower case, without accents and punctuation: "Amélie!" finds "amelie". */
    fun normalize(text: String): String =
        Normalizer.normalize(text.lowercase(), Normalizer.Form.NFD)
            .replace(Regex("\\p{M}+"), "")
            .replace(Regex("[^\\p{L}\\p{N}]+"), " ")
            .trim()
}

fun MediaEntity.toMedia() = Media(
    externalApiId = externalApiId,
    mediaType = MediaType.fromId(mediaType),
    title = title.orEmpty(),
    releaseDate = releaseDate?.takeIf { it.isNotBlank() },
    posterUrl = posterUrl,
    overview = "",
    creator = creator.orEmpty(),
)
