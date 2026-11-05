package com.reviewapp.android.data.sync

import androidx.room.withTransaction
import com.reviewapp.android.data.local.offline.CollectionItemEntity
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.IdMapEntity
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ServerShadowEntity
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.remote.SyncApi
import com.reviewapp.android.data.remote.apiCall
import com.reviewapp.android.data.remote.dto.ProfileFieldsDto
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncOpDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.data.remote.dto.SyncPushRequest
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.sync.ChangesApplier.toEntity
import com.reviewapp.android.domain.model.UnlockedAchievement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlin.random.Random

/** How sending the queue ended. */
sealed interface PushOutcome {
    /**
     * Everything that can go now went (some may wait for a retry or sit in Sync issues). [waited] and
     * [waitedIssues] count the changes that had waited a while (made offline, not just now), [unlocked] the
     * achievements the run earned, [overwritten] the profile fields whose value from elsewhere mine replaced.
     */
    data class Done(
        val applied: Int,
        val issues: Int,
        val waited: Int = 0,
        val waitedIssues: Int = 0,
        val unlocked: Int = 0,
        val overwritten: List<String> = emptyList(),
    ) : PushOutcome
    data object Offline : PushOutcome
    /** The session is gone (401): everything stays queued for after the login. */
    data object NeedsLogin : PushOutcome
    /** Too many requests: the backend said when to come back. */
    data class Paused(val untilMillis: Long) : PushOutcome
    data class Failed(val message: String) : PushOutcome
}

/**
 * Sends the outbox (`POST Sync/push`) in batches of [SyncPlanner.MAX_BATCH], in the order the changes were made, and
 * applies each answer: done ops leave the queue (a create's temporary id becomes the real one everywhere), the
 * backend's version of the entity becomes the local one once nothing else waits for it, conflicts and rejections go
 * to Sync issues, temporary failures wait for their retry. Ops are marked in flight while sent; one interrupted by
 * the app being killed goes back to waiting on the next start (the backend recognises it by its id).
 */
class PushRunner(
    private val syncApi: SyncApi,
    private val userId: () -> Int?,
    private val onUnlocked: suspend (List<UnlockedAchievement>) -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
    private val random: () -> Double = { Random.nextDouble() },
) {
    suspend fun push(db: OfflineDatabase): PushOutcome {
        val user = userId() ?: return PushOutcome.NeedsLogin
        val unlocked = ArrayList<UnlockedAchievement>()
        // Delivered once for the whole run, however it ends (one grouped banner, not a burst)
        try {
            return pushBatches(db, user, unlocked)
        } finally {
            if (unlocked.isNotEmpty()) onUnlocked(unlocked.distinctBy { it.groupCode to it.tier })
        }
    }

    private suspend fun pushBatches(db: OfflineDatabase, user: Int, unlocked: MutableList<UnlockedAchievement>): PushOutcome {
        var applied = 0
        var issues = 0
        var waited = 0
        var waitedIssues = 0
        val overwritten = LinkedHashSet<String>()
        fun done() = PushOutcome.Done(applied, issues, waited, waitedIssues, unlocked.size, overwritten.toList())
        repeat(MAX_BATCHES_PER_RUN) {
            val batch = SyncPlanner.nextBatch(db.outbox().all(), clock())
            if (batch.isEmpty()) return done()
            db.outbox().update(batch.map { it.copy(state = OutboxState.IN_FLIGHT) })

            val request = SyncPushRequest(user, batch.map { it.toDto() })
            when (val result = apiCall(useSnapshot = false) { syncApi.push(request) }) {
                is ApiResult.Error -> return failed(db, batch, result)
                is ApiResult.Success -> {
                    val response = result.data
                    db.withTransaction {
                        val byId = response.results.associateBy { it.opId }
                        for (sent in batch) {
                            // A create earlier in this batch may have mapped its temporary id meanwhile
                            val op = sent.copy(entityId = currentId(db, sent))
                            val answer = byId[op.opId]
                            if (answer == null) {
                                db.outbox().update(listOf(op.copy(state = OutboxState.PENDING)))
                                continue
                            }
                            val hadWaited = clock() - op.createdAt >= WAITED_MILLIS
                            when (val outcome = ResultInterpreter.outcome(answer, op, clock(), random())) {
                                is OpOutcome.Done -> {
                                    done(db, op, answer, outcome)
                                    applied++
                                    if (hadWaited) waited++
                                    answer.previous?.keys?.let(overwritten::addAll)
                                }
                                is OpOutcome.Issue -> {
                                    issue(db, op, answer, outcome)
                                    issues++
                                    if (hadWaited) waitedIssues++
                                }
                                is OpOutcome.Retry -> db.outbox().update(listOf(op.copy(state = OutboxState.PENDING, attempts = outcome.attempts, nextAttemptAt = outcome.nextAttemptAt, lastReason = outcome.reason)))
                                OpOutcome.Waiting -> db.outbox().update(listOf(op.copy(state = OutboxState.PENDING)))
                            }
                        }
                    }
                    unlocked += response.unlocked.map { it.toDomain() }
                }
            }
        }
        return done()
    }

    /** Ops left in flight by a run that never finished (app killed) wait again; the backend recognises them. */
    suspend fun recover(db: OfflineDatabase) = db.outbox().moveState(OutboxState.IN_FLIGHT, OutboxState.PENDING)

    private suspend fun failed(db: OfflineDatabase, batch: List<OutboxEntity>, error: ApiResult.Error): PushOutcome {
        val now = clock()
        return when {
            error.isNetwork -> {
                db.outbox().update(batch.map { it.copy(state = OutboxState.PENDING) })
                PushOutcome.Offline
            }
            error.code == 401 -> {
                db.outbox().update(batch.map { it.copy(state = OutboxState.PENDING) })
                PushOutcome.NeedsLogin
            }
            error.code == 429 -> {
                val until = now + RetryPolicy.delayMillis(1, error.retryAfterSeconds ?: DEFAULT_PAUSE_SECONDS, random())
                db.outbox().update(batch.map { it.copy(state = OutboxState.PENDING, nextAttemptAt = until) })
                PushOutcome.Paused(until)
            }
            else -> {
                // The whole request failed (a server error): each op backs off on its own
                db.outbox().update(batch.map { op ->
                    when (val outcome = ResultInterpreter.retry(op, "server_error", now, random())) {
                        is OpOutcome.Retry -> op.copy(state = OutboxState.PENDING, attempts = outcome.attempts, nextAttemptAt = outcome.nextAttemptAt, lastReason = outcome.reason)
                        is OpOutcome.Issue -> op.copy(state = outcome.state, attempts = op.attempts + 1, lastReason = outcome.reason)
                        else -> op.copy(state = OutboxState.PENDING)
                    }
                })
                PushOutcome.Failed(error.message)
            }
        }
    }

    /** The batch was read before its create got the real id: later ops of the same batch still carry the temporary one. */
    private suspend fun currentId(db: OfflineDatabase, op: OutboxEntity): Int? {
        val id = op.entityId ?: return null
        return if (id < 0) db.idMap().realId(op.entityType, id) ?: id else id
    }

    private suspend fun done(db: OfflineDatabase, op: OutboxEntity, answer: SyncOpResultDto, outcome: OpOutcome.Done) {
        db.outbox().delete(listOf(op.seq))
        var entityId = currentId(db, op)
        // A create got its real id: the row, the waiting ops and open screens (through id_map) follow
        val realId = outcome.entityId
        if (entityId != null && entityId < 0 && realId != null && (op.type == OpTypes.REVIEW_CREATE || op.type == OpTypes.COLLECTION_CREATE)) {
            if (op.type == OpTypes.REVIEW_CREATE) db.reviews().remapId(entityId, realId) else db.collections().remapId(entityId, realId)
            db.outbox().remap(op.entityType, entityId, realId)
            db.idMap().put(IdMapEntity(op.entityType, entityId, realId))
            entityId = realId
        }
        if (entityId == null) return

        val remaining = db.outbox().all().filter { it.entityType == op.entityType && it.entityId == entityId }
        val entity = answer.entity?.takeIf { it is JsonObject }
        // Nothing else waits for it: no marker any more (the backend's version below replaces the row when it came)
        if (remaining.isEmpty()) {
            when (op.entityType) {
                EntityType.REVIEW -> db.reviews().setSyncState(entityId, SyncState.SYNCED)
                EntityType.COLLECTION -> {
                    db.collections().setSyncState(entityId, SyncState.SYNCED)
                    db.collections().setItemsSyncState(entityId, SyncState.SYNCED)
                }
            }
        }
        when (op.entityType) {
            EntityType.REVIEW -> entity?.let { json ->
                val review = OpJson.decodeFromJsonElement(SyncReviewDto.serializer(), json)
                if (remaining.isEmpty()) {
                    val replies = db.reviews().replyCounts(listOf(review.id)).firstOrNull()?.replyCount ?: 0
                    db.media().upsert(listOf(review.media.toEntity(clock())))
                    db.reviews().upsert(listOf(review.toEntity().copy(replyCount = replies)))
                } else {
                    shadowAndRebase(db, op, remaining, json.toString(), review.version, outcome.merged)
                }
            }
            EntityType.COLLECTION -> entity?.let { json ->
                val collection = OpJson.decodeFromJsonElement(SyncCollectionDto.serializer(), json)
                if (remaining.isEmpty()) {
                    db.media().upsert(collection.items.map { it.media.toEntity(clock()) })
                    db.collections().upsert(listOf(collection.toEntity()))
                    db.collections().replaceItems(collection.id, collection.items.map {
                        CollectionItemEntity(collection.id, it.media.mediaType, it.media.externalApiId, it.orderIndex, it.addedAt)
                    })
                } else {
                    shadowAndRebase(db, op, remaining, json.toString(), collection.version, outcome.merged)
                }
            }
            EntityType.PROFILE -> if (remaining.isEmpty()) entity?.let { json ->
                val fields = OpJson.decodeFromJsonElement(ProfileFieldsDto.serializer(), json)
                db.profile().get(entityId)?.let { db.profile().upsert(it.copy(bio = fields.bio, profilePictureUrl = fields.profilePictureUrl?.ifEmpty { null }, syncState = SyncState.SYNCED)) }
            }
            EntityType.NOTIFICATION -> if (remaining.isEmpty()) {
                // A delete is done: the hidden row can go
                if (op.type == OpTypes.NOTIFICATION_DELETE) db.notifications().remove(entityId) else db.notifications().setSyncState(entityId, SyncState.SYNCED)
            }
            EntityType.FOLLOW -> if (remaining.isEmpty()) {
                db.follows().get(userId() ?: return, entityId)?.let { db.follows().insert(listOf(it.copy(syncState = SyncState.SYNCED))) }
            }
        }
        if (remaining.isEmpty()) db.shadows().delete(op.entityType, entityId)
    }

    /** More waits for the same thing: the backend's version becomes the shadow; the local row keeps the user's state. */
    private suspend fun shadowAndRebase(db: OfflineDatabase, op: OutboxEntity, remaining: List<OutboxEntity>, json: String, version: Long, merged: Boolean) {
        val entityId = remaining.first().entityId ?: return
        db.shadows().upsert(listOf(ServerShadowEntity(op.entityType, entityId, json, version)))
        if (!merged) db.outbox().update(remaining.filter { it.state == OutboxState.PENDING }.map { ResultInterpreter.rebase(it, version) })
    }

    private suspend fun issue(db: OfflineDatabase, op: OutboxEntity, answer: SyncOpResultDto, outcome: OpOutcome.Issue) {
        val id = currentId(db, op)
        db.outbox().update(
            listOf(
                op.copy(
                    entityId = id,
                    state = outcome.state,
                    lastReason = outcome.reason,
                    resultJson = OpJson.encodeToString(SyncOpResultDto.serializer(), answer),
                ),
            ),
        )
        id ?: return
        when (op.entityType) {
            EntityType.REVIEW -> db.reviews().setSyncState(id, SyncState.ISSUE)
            EntityType.COLLECTION -> db.collections().setSyncState(id, SyncState.ISSUE)
            EntityType.NOTIFICATION -> db.notifications().setSyncState(id, SyncState.ISSUE)
        }
    }

    private fun OutboxEntity.toDto() = SyncOpDto(
        opId = opId,
        type = type,
        clientRef = clientRef,
        dependsOn = dependencies().ifEmpty { null },
        payload = OpJson.parseToJsonElement(payloadJson).jsonObject,
    )

    companion object {
        /** A long queue continues in the next run (the rate limit lets ~20 requests through at once). */
        const val MAX_BATCHES_PER_RUN = 10
        const val DEFAULT_PAUSE_SECONDS = 30L
        /** A change older than this when its answer came had waited (made offline): the sync summary mentions it. */
        const val WAITED_MILLIS = 15_000L
    }
}
