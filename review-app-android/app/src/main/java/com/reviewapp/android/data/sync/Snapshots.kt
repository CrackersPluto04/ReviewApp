package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.SnapshotUsage
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive

/** Which GET answers are kept as offline snapshots, and under which key (pure, unit-tested). */
object SnapshotPolicy {

    /** Never answered from the device: sessions, payments, account security, the AI, the sync itself, live searches. */
    private val excludedPrefixes = listOf(
        "Auth/", "Security/", "Subscription/", "Recognition/", "Sync/",
        "Media/search", "User/search",
        // The user's own notifications and memberships come from the offline database
        "Notification", "Collection/me/", "Review/check",
    )

    /**
     * The snapshot key of a request: its path below `api/` with the query (`User/bob/reviews?page=1&...`), or null
     * when the request is never kept.
     */
    fun keyOf(apiPath: String, query: String?): String? {
        if (excludedPrefixes.any { apiPath.startsWith(it) }) return null
        return if (query.isNullOrEmpty()) apiPath else "$apiPath?$query"
    }
}

/** The other users' reviews and collections a snapshot shows, so hidden ones can be pruned. */
data class SnapshotContent(val reviewIds: Set<Int> = emptySet(), val collectionIds: Set<Int> = emptySet()) {
    /** The review ids as a sorted, comma-separated string, the way they are stored. */
    fun reviewsCsv() = reviewIds.sorted().joinToString(",")
    /** The collection ids as a sorted, comma-separated string, the way they are stored. */
    fun collectionsCsv() = collectionIds.sorted().joinToString(",")

    companion object {
        /** The ids of a stored comma-separated string; unreadable parts are skipped. */
        fun parseIds(csv: String): Set<Int> = csv.split(',').mapNotNullTo(HashSet()) { it.trim().toIntOrNull() }
    }
}

/** Finds out which reviews and collections of other users a snapshot shows, from its key and body. */
object SnapshotIndex {
    private val json = Json { ignoreUnknownKeys = true }
    private val reviewDetail = Regex("^Review/(\\d+)$")
    private val collectionDetail = Regex("^Collection/(\\d+)$")
    private val userReviews = Regex("^User/[^/]+/reviews$")
    private val userCollections = Regex("^User/[^/]+/collections$")
    private val replyById = Regex("^ReviewReply/\\d+$")

    /** What [body], the answer of [key], shows (see [SnapshotPolicy.keyOf] for the key's shape). */
    fun contentOf(key: String, body: String): SnapshotContent {
        val path = key.substringBefore('?')
        val query = key.substringAfter('?', "")
        return try {
            when {
                reviewDetail.matches(path) -> SnapshotContent(reviewIds = setOf(reviewDetail.find(path)!!.groupValues[1].toInt()))
                collectionDetail.matches(path) -> SnapshotContent(collectionIds = setOf(collectionDetail.find(path)!!.groupValues[1].toInt()))
                path.startsWith("Review/media/") || userReviews.matches(path) -> SnapshotContent(reviewIds = ids(items(body)))
                path == "Review/recent" -> SnapshotContent(reviewIds = ids(json.parseToJsonElement(body)))
                userCollections.matches(path) -> SnapshotContent(collectionIds = ids(json.parseToJsonElement(body)))
                // A reply thread belongs to its review
                path == "ReviewReply" -> SnapshotContent(reviewIds = setOfNotNull(queryParam(query, "reviewId")?.toIntOrNull()))
                replyById.matches(path) -> SnapshotContent(reviewIds = setOfNotNull(intField(json.parseToJsonElement(body), "reviewID")))
                else -> SnapshotContent()
            }
        } catch (_: Exception) {
            SnapshotContent()
        }
    }

    private fun items(body: String): JsonElement? = (json.parseToJsonElement(body) as? JsonObject)?.get("items")

    private fun ids(element: JsonElement?): Set<Int> =
        (element as? JsonArray)?.mapNotNullTo(HashSet()) { intField(it, "id") } ?: emptySet()

    private fun intField(element: JsonElement, name: String): Int? =
        (element as? JsonObject)?.get(name)?.jsonPrimitive?.intOrNull

    private fun queryParam(query: String, name: String): String? =
        query.split('&').firstOrNull { it.substringBefore('=').equals(name, ignoreCase = true) }?.substringAfter('=', "")
}

/** The offline cache's storage budget: the least recently used snapshots go first. */
object SnapshotEviction {
    /** The keys to delete so the rest fits [budgetBytes], oldest use first; nothing when it already fits. */
    fun keysToEvict(usage: List<SnapshotUsage>, budgetBytes: Long): List<String> {
        var total = usage.sumOf { it.sizeBytes }
        if (total <= budgetBytes) return emptyList()
        val evicted = mutableListOf<String>()
        for (snapshot in usage.sortedBy { it.lastUsedAt }) {
            if (total <= budgetBytes) break
            evicted += snapshot.key
            total -= snapshot.sizeBytes
        }
        return evicted
    }
}
