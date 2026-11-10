package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.CollectionUpdatePayload
import com.reviewapp.android.data.remote.dto.NotificationUpToPayload
import com.reviewapp.android.data.remote.dto.ProfileFieldsDto
import com.reviewapp.android.data.remote.dto.ProfileUpdatePayload
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.data.remote.dto.ReviewDeletePayload
import com.reviewapp.android.data.remote.dto.ReviewUpdatePayload
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlin.math.min
import kotlin.math.pow

/** The op types of `POST Sync/push` (the API's SyncOpTypes). */
object OpTypes {
    const val REVIEW_CREATE = "review.create"
    const val REVIEW_UPDATE = "review.update"
    const val REVIEW_DELETE = "review.delete"
    const val COLLECTION_CREATE = "collection.create"
    const val COLLECTION_UPDATE = "collection.update"
    const val COLLECTION_DELETE = "collection.delete"
    const val COLLECTION_ADD_ITEM = "collection.addItem"
    const val COLLECTION_REMOVE_ITEM = "collection.removeItem"
    const val COLLECTION_REORDER = "collection.reorder"
    const val PROFILE_UPDATE = "profile.update"
    const val FOLLOW_SET = "follow.set"
    const val FOLLOWER_REMOVE = "follower.remove"
    const val NOTIFICATION_SEEN = "notification.seen"
    const val NOTIFICATION_DELETE = "notification.delete"
    const val NOTIFICATION_MARK_ALL_SEEN = "notification.markAllSeen"
    const val NOTIFICATION_DELETE_SEEN = "notification.deleteSeen"
}

/** How op payloads go to JSON: nulls are left out (an absent field = unchanged / not sent). */
val OpJson = Json {
    explicitNulls = false
    encodeDefaults = true
    ignoreUnknownKeys = true
}

/** The thing an op changes: ops of one key are kept in order, combined and blocked together. */
fun OutboxEntity.entityKey(): String = if (entityId == null) "bulk:$type" else "$entityType:$entityId"

/** The opIds this op waits for. */
fun OutboxEntity.dependencies(): List<String> = dependsOn?.split(',')?.filter { it.isNotBlank() }.orEmpty()

/** What [OutboxCompactor.add] does to the queue. */
data class Compaction(
    /** The `seq` of queued ops to remove. */
    val delete: List<Long> = emptyList(),

    /** Queued ops to replace with these versions. */
    val update: List<OutboxEntity> = emptyList(),
    /** The new op, when it isn't folded into an earlier one. */
    val insert: OutboxEntity? = null,
)

/**
 * Combines a new change with the not yet sent changes of the same thing, so the backend gets the end result of
 * what the user did (pure, unit-tested). Only the waiting ([OutboxState.PENDING]) ops after the last one already
 * sent or in Sync issues are touched, so nothing that may already be applied is changed.
 */
object OutboxCompactor {

    /** The queue changes that bring [op] into [queue]: fold it into an earlier op, cancel ops out, or just append it. */
    fun add(queue: List<OutboxEntity>, op: OutboxEntity): Compaction {
        val sameKey = queue.filter { it.entityKey() == op.entityKey() }.sortedBy { it.seq }
        val lastLocked = sameKey.lastOrNull { it.state != OutboxState.PENDING }?.seq ?: Long.MIN_VALUE
        val tail = sameKey.filter { it.seq > lastLocked }
        return when (op.type) {
            OpTypes.REVIEW_UPDATE -> foldUpdate(tail, op, OpTypes.REVIEW_CREATE, OpTypes.REVIEW_UPDATE, ::reviewIntoCreate, ::reviewIntoUpdate)
            OpTypes.COLLECTION_UPDATE -> foldUpdate(tail, op, OpTypes.COLLECTION_CREATE, OpTypes.COLLECTION_UPDATE, ::collectionIntoCreate, ::collectionIntoUpdate)
            OpTypes.REVIEW_DELETE, OpTypes.COLLECTION_DELETE -> delete(tail, op)
            OpTypes.COLLECTION_ADD_ITEM, OpTypes.COLLECTION_REMOVE_ITEM -> item(tail, op)
            OpTypes.COLLECTION_REORDER -> replaceSameType(tail, op)
            OpTypes.PROFILE_UPDATE -> profile(tail, op)
            OpTypes.FOLLOW_SET, OpTypes.NOTIFICATION_SEEN -> replaceSameType(tail, op)
            OpTypes.NOTIFICATION_DELETE -> Compaction(
                delete = tail.filter { it.type == OpTypes.NOTIFICATION_SEEN }.map { it.seq },
                insert = op.takeIf { tail.none { it.type == OpTypes.NOTIFICATION_DELETE } },
            )
            OpTypes.NOTIFICATION_MARK_ALL_SEEN, OpTypes.NOTIFICATION_DELETE_SEEN -> upTo(tail, op)
            else -> Compaction(insert = op)
        }
    }

    // An edit of something not sent yet goes into its create; a second edit into the first one (its base stays)
    private fun foldUpdate(
        tail: List<OutboxEntity>,
        op: OutboxEntity,
        createType: String,
        updateType: String,
        intoCreate: (OutboxEntity, OutboxEntity) -> OutboxEntity,
        intoUpdate: (OutboxEntity, OutboxEntity) -> OutboxEntity,
    ): Compaction {
        tail.lastOrNull { it.type == createType }?.let { return Compaction(update = listOf(intoCreate(it, op))) }
        tail.lastOrNull { it.type == updateType }?.let { return Compaction(update = listOf(intoUpdate(it, op))) }
        return Compaction(insert = op)
    }

    private fun reviewIntoCreate(create: OutboxEntity, update: OutboxEntity): OutboxEntity {
        val payload = OpJson.decodeFromString(ReviewCreatePayload.serializer(), create.payloadJson)
        val fields = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), update.payloadJson).fields
        return create.copy(payloadJson = OpJson.encodeToString(ReviewCreatePayload.serializer(), payload.copy(fields = fields)))
    }

    private fun reviewIntoUpdate(first: OutboxEntity, next: OutboxEntity): OutboxEntity {
        val payload = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), first.payloadJson)
        val fields = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), next.payloadJson).fields
        return first.copy(payloadJson = OpJson.encodeToString(ReviewUpdatePayload.serializer(), payload.copy(fields = fields)))
    }

    private fun collectionIntoCreate(create: OutboxEntity, update: OutboxEntity): OutboxEntity {
        val fields = OpJson.decodeFromString(CollectionUpdatePayload.serializer(), update.payloadJson).fields
        return create.copy(payloadJson = OpJson.encodeToString(CollectionCreatePayload.serializer(), CollectionCreatePayload(fields)))
    }

    private fun collectionIntoUpdate(first: OutboxEntity, next: OutboxEntity): OutboxEntity {
        val payload = OpJson.decodeFromString(CollectionUpdatePayload.serializer(), first.payloadJson)
        val fields = OpJson.decodeFromString(CollectionUpdatePayload.serializer(), next.payloadJson).fields
        return first.copy(payloadJson = OpJson.encodeToString(CollectionUpdatePayload.serializer(), payload.copy(fields = fields)))
    }

    // Created and deleted before any sync: nothing ever reaches the backend. Otherwise waiting edits are moot.
    private fun delete(tail: List<OutboxEntity>, op: OutboxEntity): Compaction {
        val createType = if (op.type == OpTypes.REVIEW_DELETE) OpTypes.REVIEW_CREATE else OpTypes.COLLECTION_CREATE
        if (tail.any { it.type == createType }) return Compaction(delete = tail.map { it.seq })
        if (op.type == OpTypes.REVIEW_DELETE) {
            // The version the deleted review was seen at is the one before the waiting edit
            val update = tail.lastOrNull { it.type == OpTypes.REVIEW_UPDATE }
            val baseVersion = update?.let { OpJson.decodeFromString(ReviewUpdatePayload.serializer(), it.payloadJson).baseVersion }
            val payload = OpJson.decodeFromString(ReviewDeletePayload.serializer(), op.payloadJson)
            val delete = if (update != null) {
                op.copy(payloadJson = OpJson.encodeToString(ReviewDeletePayload.serializer(), payload.copy(baseVersion = baseVersion)))
            } else {
                op
            }
            return Compaction(delete = tail.map { it.seq }, insert = delete)
        }
        return Compaction(delete = tail.map { it.seq }, insert = op)
    }

    // Adding and removing the same media cancel out
    private fun item(tail: List<OutboxEntity>, op: OutboxEntity): Compaction {
        val media = OpJson.decodeFromString(CollectionItemPayload.serializer(), op.payloadJson).media
        val sameMedia = tail.filter {
            (it.type == OpTypes.COLLECTION_ADD_ITEM || it.type == OpTypes.COLLECTION_REMOVE_ITEM) &&
                OpJson.decodeFromString(CollectionItemPayload.serializer(), it.payloadJson).media == media
        }
        val last = sameMedia.lastOrNull() ?: return Compaction(insert = op)
        return if (last.type == op.type) Compaction() else Compaction(delete = listOf(last.seq))
    }

    // Only the last one counts (a reorder goes to the end, after the adds it may list)
    private fun replaceSameType(tail: List<OutboxEntity>, op: OutboxEntity): Compaction =
        Compaction(delete = tail.filter { it.type == op.type }.map { it.seq }, insert = op)

    private fun profile(tail: List<OutboxEntity>, op: OutboxEntity): Compaction {
        val earlier = tail.lastOrNull { it.type == OpTypes.PROFILE_UPDATE } ?: return Compaction(insert = op)
        val first = OpJson.decodeFromString(ProfileUpdatePayload.serializer(), earlier.payloadJson)
        val next = OpJson.decodeFromString(ProfileUpdatePayload.serializer(), op.payloadJson).fields
        val fields = ProfileFieldsDto(bio = next.bio ?: first.fields.bio, profilePictureUrl = next.profilePictureUrl ?: first.fields.profilePictureUrl)
        return Compaction(update = listOf(earlier.copy(payloadJson = OpJson.encodeToString(ProfileUpdatePayload.serializer(), first.copy(fields = fields)))))
    }

    private fun upTo(tail: List<OutboxEntity>, op: OutboxEntity): Compaction {
        val earlier = tail.lastOrNull { it.type == op.type } ?: return Compaction(insert = op)
        val upTo = maxOf(upToOf(earlier), upToOf(op))
        return Compaction(update = listOf(earlier.copy(payloadJson = OpJson.encodeToString(NotificationUpToPayload.serializer(), NotificationUpToPayload(upTo)))))
    }

    private fun upToOf(op: OutboxEntity) = OpJson.decodeFromString(NotificationUpToPayload.serializer(), op.payloadJson).upToId
}

/** Which waiting ops go in the next push. */
object SyncPlanner {
    /** The API's limit per request. */
    const val MAX_BATCH = 50

    /**
     * The ops to send now, in queue order. An op that can't go (in Sync issues, waiting for its retry, or behind an
     * op that can't go) blocks the later ops of the same thing, so they are never applied on a wrong base; an op
     * whose dependency is still queued but not in this batch waits too.
     */
    fun nextBatch(queue: List<OutboxEntity>, nowMillis: Long, max: Int = MAX_BATCH): List<OutboxEntity> {
        val queued = queue.mapTo(HashSet()) { it.opId }
        val blocked = HashSet<String>()
        val batch = ArrayList<OutboxEntity>()
        val inBatch = HashSet<String>()
        for (op in queue.sortedBy { it.seq }) {
            val key = op.entityKey()
            val ready = op.state == OutboxState.PENDING && op.nextAttemptAt <= nowMillis
            val dependenciesOk = op.dependencies().all { it !in queued || it in inBatch }
            if (key in blocked || !ready || !dependenciesOk) {
                blocked += key
                continue
            }
            batch += op
            inBatch += op.opId
            if (batch.size >= max) break
        }
        return batch
    }
}

/** When a failed op is tried again. */
object RetryPolicy {
    /** The delay before the first retry; it doubles from there. */
    const val FIRST_DELAY_MILLIS = 5_000L

    /** The longest delay between two attempts. */
    const val MAX_DELAY_MILLIS = 60 * 60_000L
    /** A `server_error` stops being retried (it goes to Sync issues) after this many attempts. */
    const val MAX_SERVER_ERRORS = 8

    /** [attempt] counts from 1; [random] in [0, 1) spreads devices out (±20 %); a `Retry-After` is a minimum. */
    fun delayMillis(attempt: Int, retryAfterSeconds: Long? = null, random: Double = 0.5): Long {
        val exponential = min(MAX_DELAY_MILLIS.toDouble(), FIRST_DELAY_MILLIS * 2.0.pow((attempt - 1).coerceAtLeast(0)))
        val jittered = (exponential * (0.8 + 0.4 * random)).toLong()
        return maxOf(jittered, (retryAfterSeconds ?: 0) * 1000)
    }
}

/** What to do with a queued op after the backend's answer. */
sealed interface OpOutcome {
    /** Applied (or merged): the op leaves the queue; [entityId] is the real id of what it created. */
    data class Done(val entityId: Int?, val merged: Boolean) : OpOutcome
    /** Needs the user (Sync issues), kept with the answer. */
    data class Issue(val state: Int, val reason: String?) : OpOutcome
    /** Try again later. */
    data class Retry(val nextAttemptAt: Long, val attempts: Int, val reason: String?) : OpOutcome
    /** Not processed because an op it depends on wasn't applied: it waits, as it was. */
    data object Waiting : OpOutcome
}

/** Turns the backend's answer to one op into what the queue does with it (pure, unit-tested). */
object ResultInterpreter {
    /** The outcome of [result]; a status this app version does not know is treated as a retry. */
    fun outcome(result: SyncOpResultDto, op: OutboxEntity, nowMillis: Long, random: Double = 0.5): OpOutcome = when (result.status) {
        "applied" -> OpOutcome.Done(result.entityId, merged = false)
        "merged" -> OpOutcome.Done(result.entityId, merged = true)
        "conflict" -> OpOutcome.Issue(OutboxState.CONFLICT, result.reason)
        "rejected" -> OpOutcome.Issue(OutboxState.REJECTED, result.reason)
        "skipped" -> OpOutcome.Waiting
        // "retry" and anything this app version doesn't know yet
        else -> retry(op, result.reason, nowMillis, random, retryAfterSeconds = null)
    }

    /** A temporary failure; repeated server errors end in Sync issues instead of retrying forever. */
    fun retry(op: OutboxEntity, reason: String?, nowMillis: Long, random: Double = 0.5, retryAfterSeconds: Long? = null): OpOutcome {
        val attempts = op.attempts + 1
        if (reason == "server_error" && attempts >= RetryPolicy.MAX_SERVER_ERRORS) return OpOutcome.Issue(OutboxState.REJECTED, reason)
        return OpOutcome.Retry(nowMillis + RetryPolicy.delayMillis(attempts, retryAfterSeconds, random), attempts, reason)
    }

    /**
     * After an op was applied unchanged, the waiting ops of the same thing were made on top of it: their base
     * version becomes the new one (a merged result keeps the old base, so the server merges them again).
     */
    fun rebase(op: OutboxEntity, newVersion: Long): OutboxEntity {
        val payload = OpJson.parseToJsonElement(op.payloadJson).jsonObject
        if (!payload.containsKey("baseVersion")) return op
        val rebased = JsonObject(payload + ("baseVersion" to JsonPrimitive(newVersion)))
        return op.copy(payloadJson = rebased.toString())
    }
}

/** The entity type an op type belongs to. */
fun entityTypeOf(opType: String): Int = when {
    opType.startsWith("review.") -> EntityType.REVIEW
    opType.startsWith("collection.") -> EntityType.COLLECTION
    opType == OpTypes.PROFILE_UPDATE -> EntityType.PROFILE
    opType == OpTypes.FOLLOW_SET -> EntityType.FOLLOW
    opType == OpTypes.FOLLOWER_REMOVE -> EntityType.FOLLOWER
    else -> EntityType.NOTIFICATION
}
