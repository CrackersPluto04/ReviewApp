package com.reviewapp.android.data.sync

import androidx.room.withTransaction
import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.CollectionItemEntity
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.IdMapEntity
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.local.offline.ServerShadowEntity
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.CollectionUpdatePayload
import com.reviewapp.android.data.remote.dto.EntityRefDto
import com.reviewapp.android.data.remote.dto.MediaKeyDto
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.data.remote.dto.ReviewDeletePayload
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewUpdatePayload
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.sync.ChangesApplier.toEntity
import com.reviewapp.android.util.Validation
import java.util.UUID

/** A change in Sync issues with what the conflict screen needs. */
data class IssueDetails(
    val op: OutboxEntity,
    val kind: IssueKind,
    val answer: SyncOpResultDto?,
    /** The local row the change is about (null when it was deleted here afterwards, or never was a review / collection). */
    val localReview: ReviewEntity? = null,
    val localCollection: CollectionEntity? = null,
    /** The backend's version from the answer. */
    val serverReview: SyncReviewDto? = null,
    val serverCollection: SyncCollectionDto? = null,
    /** My fields: the local row (all my edits since), else the change itself. */
    val mineReview: ReviewFieldsDto? = null,
    val mineCollection: CollectionFieldsDto? = null,
    /** Later changes of the same thing include a delete (some choices make no sense then). */
    val deletedHereLater: Boolean = false,
)

/** How a decision ended. */
sealed interface Resolution {
    data object Done : Resolution
    /** Synced or discarded meanwhile. */
    data object Gone : Resolution
    data class Invalid(val message: String) : Resolution
}

/**
 * Applies the user's decision on a change in Sync issues (a conflict or a rejection) to the offline copy and the
 * queue, in one transaction, then asks the sync to run. Every decision ends in either changes queued again (on top
 * of the backend's current version, so the backend applies them as they are) or the backend's version shown here.
 */
class ConflictResolver(
    private val database: () -> OfflineDatabase?,
    private val writer: OfflineWriter,
    private val onQueued: () -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
) {

    suspend fun details(seq: Long): IssueDetails? {
        val db = database() ?: return null
        val queue = db.outbox().all()
        val op = queue.firstOrNull { it.seq == seq }?.takeIf { it.isIssue() } ?: return null
        val answer = op.resultJson?.let { runCatching { OpJson.decodeFromString(SyncOpResultDto.serializer(), it) }.getOrNull() }
        val later = queue.filter { it.entityKey() == op.entityKey() && it.seq > op.seq }
        val deletedLater = later.any { it.type == OpTypes.REVIEW_DELETE || it.type == OpTypes.COLLECTION_DELETE }
        val kind = IssueRules.kindOf(op, answer)
        val id = op.entityId
        return when (entityTypeOf(op.type)) {
            EntityType.REVIEW -> {
                val row = id?.let { db.reviews().entity(it) }
                IssueDetails(
                    op, kind, answer,
                    localReview = row,
                    serverReview = answer?.serverEntity(SyncReviewDto.serializer()),
                    mineReview = row?.fields() ?: reviewFieldsOf(op),
                    deletedHereLater = deletedLater,
                )
            }
            EntityType.COLLECTION -> {
                val row = id?.let { db.collections().entity(it) }
                IssueDetails(
                    op, kind, answer,
                    localCollection = row,
                    serverCollection = answer?.serverEntity(SyncCollectionDto.serializer()),
                    mineCollection = row?.let { CollectionFieldsDto(it.name, it.visibility) } ?: collectionFieldsOf(op),
                    deletedHereLater = deletedLater,
                )
            }
            else -> IssueDetails(op, kind, answer)
        }
    }

    /** A field conflict decided field by field: sent again on top of the backend's version. */
    suspend fun resolveFields(seq: Long, choices: Map<String, FieldChoice>): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val id = op.entityId ?: return@resolve invalid()
        when (op.type) {
            OpTypes.REVIEW_UPDATE -> {
                val server = details.serverReview ?: return@resolve invalid()
                val payload = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), op.payloadJson)
                val resolved = IssueRules.resolveReview(payload.base, details.mineReview ?: payload.fields, server.fields(), choices)
                reviewFieldsError(resolved)?.let { return@resolve Resolution.Invalid(it) }
                // The local row holds my later edits too: the resolved change replaces them
                if (details.localReview != null) dropLater(db, op, OpTypes.REVIEW_UPDATE)
                requeue(db, op, OpJson.encodeToString(ReviewUpdatePayload.serializer(), payload.copy(baseVersion = server.version, base = server.fields(), fields = resolved)))
                details.localReview?.let { row ->
                    db.reviews().upsert(listOf(row.withFields(resolved).copy(version = server.version, syncState = SyncState.PENDING)))
                }
                shadow(db, EntityType.REVIEW, id, OpJson.encodeToString(SyncReviewDto.serializer(), server), server.version)
            }
            OpTypes.COLLECTION_UPDATE -> {
                val server = details.serverCollection ?: return@resolve invalid()
                val payload = OpJson.decodeFromString(CollectionUpdatePayload.serializer(), op.payloadJson)
                val theirs = CollectionFieldsDto(server.name, server.visibility)
                val resolved = IssueRules.resolveCollection(payload.base, details.mineCollection ?: payload.fields, theirs, choices)
                collectionNameError(db, resolved.name, exceptId = id)?.let { return@resolve Resolution.Invalid(it) }
                if (details.localCollection != null) dropLater(db, op, OpTypes.COLLECTION_UPDATE)
                requeue(db, op, OpJson.encodeToString(CollectionUpdatePayload.serializer(), payload.copy(baseVersion = server.version, base = theirs, fields = resolved)))
                details.localCollection?.let { row ->
                    db.collections().upsert(listOf(row.copy(name = resolved.name, visibility = resolved.visibility, version = server.version, syncState = SyncState.PENDING)))
                }
                shadow(db, EntityType.COLLECTION, id, OpJson.encodeToString(SyncCollectionDto.serializer(), server), server.version)
            }
            else -> return@resolve invalid()
        }
        Resolution.Done
    }

    /**
     * Deleted elsewhere, recreated from what this phone shows: a new create (a temporary id again), and for a
     * collection its items added after it.
     */
    suspend fun recreate(seq: Long): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val id = op.entityId ?: return@resolve invalid()
        if (details.kind != IssueKind.DeletedElsewhere) return@resolve invalid()
        when (entityTypeOf(op.type)) {
            EntityType.REVIEW -> {
                val row = details.localReview ?: return@resolve Resolution.Invalid(DELETED_HERE)
                val tempId = writer.nextTempId(db)
                db.outbox().delete(opsOf(db, op).map { it.seq })
                db.shadows().delete(EntityType.REVIEW, id)
                db.reviews().remapId(id, tempId)
                db.reviews().upsert(listOf(row.copy(id = tempId, version = null, syncState = SyncState.PENDING)))
                val payload = ReviewCreatePayload(MediaKeyDto(row.mediaType, row.externalApiId), row.fields(), createdAt = row.createdAt)
                insertCreate(db, OpTypes.REVIEW_CREATE, EntityType.REVIEW, tempId, OpJson.encodeToString(ReviewCreatePayload.serializer(), payload))
            }
            EntityType.COLLECTION -> {
                val row = details.localCollection ?: return@resolve Resolution.Invalid(DELETED_HERE)
                collectionNameError(db, row.name, exceptId = id)?.let { return@resolve Resolution.Invalid(it) }
                val tempId = writer.nextTempId(db)
                db.outbox().delete(opsOf(db, op).map { it.seq })
                db.shadows().delete(EntityType.COLLECTION, id)
                // The items follow through the foreign key
                db.collections().remapId(id, tempId)
                db.collections().upsert(listOf(row.copy(id = tempId, version = null, syncState = SyncState.PENDING)))
                db.collections().setItemsSyncState(tempId, SyncState.PENDING)
                val payload = CollectionCreatePayload(CollectionFieldsDto(row.name, row.visibility))
                val create = insertCreate(db, OpTypes.COLLECTION_CREATE, EntityType.COLLECTION, tempId, OpJson.encodeToString(CollectionCreatePayload.serializer(), payload))
                for (item in db.collections().itemsOf(tempId)) {
                    val add = CollectionItemPayload(EntityRefDto(ref = create.clientRef), MediaKeyDto(item.mediaType, item.externalApiId))
                    insert(db, OpTypes.COLLECTION_ADD_ITEM, EntityType.COLLECTION, tempId, OpJson.encodeToString(CollectionItemPayload.serializer(), add), dependsOn = create.opId)
                }
            }
            else -> return@resolve invalid()
        }
        Resolution.Done
    }

    /** A review deleted here was edited elsewhere since: delete it anyway (on top of that edit). */
    suspend fun deleteAnyway(seq: Long): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val server = details.serverReview
        if (op.type != OpTypes.REVIEW_DELETE || server == null) return@resolve invalid()
        val payload = OpJson.decodeFromString(ReviewDeletePayload.serializer(), op.payloadJson)
        requeue(db, op, OpJson.encodeToString(ReviewDeletePayload.serializer(), payload.copy(baseVersion = server.version)))
        shadow(db, EntityType.REVIEW, server.id, OpJson.encodeToString(SyncReviewDto.serializer(), server), server.version)
        Resolution.Done
    }

    /**
     * Mine wins over a review written elsewhere: my review becomes an edit of that one (its id from now on), sent
     * on top of its current version.
     */
    suspend fun overwriteWithMine(seq: Long): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val server = details.serverReview
        val row = details.localReview
        val tempId = op.entityId
        if (details.kind != IssueKind.AlreadyReviewed || server == null || tempId == null) return@resolve invalid()
        if (row == null) return@resolve Resolution.Invalid(DELETED_HERE)
        db.outbox().delete(opsOf(db, op).map { it.seq })
        db.reviews().delete(listOf(server.id))
        db.reviews().remapId(tempId, server.id)
        db.reviews().upsert(listOf(row.copy(id = server.id, createdAt = server.createdAt, version = server.version, syncState = SyncState.PENDING)))
        db.media().upsert(listOf(server.media.toEntity(clock())))
        db.idMap().put(IdMapEntity(EntityType.REVIEW, tempId, server.id))
        shadow(db, EntityType.REVIEW, server.id, OpJson.encodeToString(SyncReviewDto.serializer(), server), server.version)
        val payload = ReviewUpdatePayload(EntityRefDto(id = server.id), baseVersion = server.version, base = server.fields(), fields = row.fields())
        insert(db, OpTypes.REVIEW_UPDATE, EntityType.REVIEW, server.id, OpJson.encodeToString(ReviewUpdatePayload.serializer(), payload))
        Resolution.Done
    }

    /** A taken collection name: try another one. */
    suspend fun rename(seq: Long, name: String): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val id = op.entityId ?: return@resolve invalid()
        val trimmed = name.trim()
        collectionNameError(db, trimmed, exceptId = id)?.let { return@resolve Resolution.Invalid(it) }
        when (op.type) {
            OpTypes.COLLECTION_CREATE -> {
                val payload = OpJson.decodeFromString(CollectionCreatePayload.serializer(), op.payloadJson)
                requeue(db, op, OpJson.encodeToString(CollectionCreatePayload.serializer(), payload.copy(fields = payload.fields.copy(name = trimmed))))
            }
            OpTypes.COLLECTION_UPDATE -> {
                val payload = OpJson.decodeFromString(CollectionUpdatePayload.serializer(), op.payloadJson)
                requeue(db, op, OpJson.encodeToString(CollectionUpdatePayload.serializer(), payload.copy(fields = payload.fields.copy(name = trimmed))))
            }
            else -> return@resolve invalid()
        }
        details.localCollection?.let { db.collections().upsert(listOf(it.copy(name = trimmed, syncState = SyncState.PENDING))) }
        Resolution.Done
    }

    /**
     * A collection created here whose name is taken: its items go into the existing collection of that name (the
     * waiting additions and removals now point at it), the one made here disappears.
     */
    suspend fun mergeIntoExisting(seq: Long): Resolution = resolve(seq) { db, details ->
        val op = details.op
        val existing = details.serverCollection
        val tempId = op.entityId
        if (op.type != OpTypes.COLLECTION_CREATE || details.kind != IssueKind.NameTaken || existing == null || tempId == null) return@resolve invalid()
        if (details.deletedHereLater) return@resolve Resolution.Invalid(DELETED_HERE)
        val clientRef = op.clientRef ?: return@resolve invalid()

        // The existing collection, as the backend has it, if this phone doesn't have it yet
        if (db.collections().entity(existing.id) == null) {
            db.media().upsert(existing.items.map { it.media.toEntity(clock()) })
            db.collections().upsert(listOf(existing.toEntity()))
            db.collections().replaceItems(existing.id, existing.items.map { CollectionItemEntity(existing.id, it.media.mediaType, it.media.externalApiId, it.orderIndex, it.addedAt) })
        }
        val present = db.collections().itemsOf(existing.id)
        var order = (present.maxOfOrNull { it.orderIndex } ?: -1) + 1
        val moved = db.collections().itemsOf(tempId)
            .filter { item -> present.none { it.mediaType == item.mediaType && it.externalApiId == item.externalApiId } }
            .map { it.copy(collectionId = existing.id, orderIndex = order++, syncState = SyncState.PENDING) }
        db.collections().delete(listOf(tempId))
        if (moved.isNotEmpty()) db.collections().insertItems(moved)

        val ops = opsOf(db, op)
        val kept = ops.filter { it.type == OpTypes.COLLECTION_ADD_ITEM || it.type == OpTypes.COLLECTION_REMOVE_ITEM }
            .map { it.copy(entityId = existing.id, payloadJson = IssueRules.retarget(it.payloadJson, clientRef, existing.id), dependsOn = IssueRules.withoutDependency(it.dependsOn, op.opId)) }
        // The create, and a rename or reorder of the one made here (the existing one keeps its name and order)
        db.outbox().delete(ops.filter { o -> kept.none { it.seq == o.seq } }.map { it.seq })
        if (kept.isNotEmpty()) {
            db.outbox().update(kept)
            if (db.shadows().get(EntityType.COLLECTION, existing.id) == null) {
                shadow(db, EntityType.COLLECTION, existing.id, OpJson.encodeToString(SyncCollectionDto.serializer(), existing), existing.version)
            }
            db.collections().setSyncState(existing.id, SyncState.PENDING)
        }
        db.idMap().put(IdMapEntity(EntityType.COLLECTION, tempId, existing.id))
        Resolution.Done
    }

    /** A rejection that may work now (the server kept failing): sent again. */
    suspend fun retry(seq: Long): Resolution = resolve(seq) { db, details ->
        requeue(db, details.op, details.op.payloadJson)
        Resolution.Done
    }

    /**
     * Throws my change away (and the later changes of the same thing) and shows the backend's version: the one in
     * the answer when it came with one (the other review of the title, the existing collection), else the last one
     * this phone knows; gone when it was deleted there.
     */
    suspend fun useTheirs(seq: Long): Resolution {
        val details = details(seq) ?: return Resolution.Gone
        val result = writer.discard(seq) { db ->
            val id = details.op.entityId ?: return@discard
            when (entityTypeOf(details.op.type)) {
                EntityType.REVIEW -> when {
                    details.kind == IssueKind.DeletedElsewhere -> db.reviews().delete(listOf(id))
                    details.serverReview != null -> {
                        val server = details.serverReview
                        val replies = db.reviews().replyCounts(listOf(server.id)).firstOrNull()?.replyCount ?: 0
                        db.reviews().delete(listOf(id))
                        db.media().upsert(listOf(server.media.toEntity(clock())))
                        db.reviews().upsert(listOf(server.toEntity().copy(replyCount = replies)))
                    }
                }
                EntityType.COLLECTION -> when {
                    details.kind == IssueKind.DeletedElsewhere -> db.collections().delete(listOf(id))
                    // Not over changes still waiting for that one (the existing collection of a taken name)
                    details.serverCollection != null && db.outbox().all().none { it.entityType == EntityType.COLLECTION && it.entityId == details.serverCollection.id } -> {
                        val server = details.serverCollection
                        db.media().upsert(server.items.map { it.media.toEntity(clock()) })
                        db.collections().upsert(listOf(server.toEntity()))
                        db.collections().replaceItems(server.id, server.items.map { CollectionItemEntity(server.id, it.media.mediaType, it.media.externalApiId, it.orderIndex, it.addedAt) })
                    }
                }
            }
        }
        return when (result) {
            DiscardResult.Discarded -> Resolution.Done
            DiscardResult.NotFound -> Resolution.Gone
            DiscardResult.Sending -> Resolution.Invalid("It's being sent right now. Try again in a moment.")
        }
    }

    // Helpers

    private suspend fun resolve(seq: Long, block: suspend (OfflineDatabase, IssueDetails) -> Resolution): Resolution {
        val db = database() ?: return Resolution.Gone
        val result = db.withTransaction {
            val details = details(seq) ?: return@withTransaction Resolution.Gone
            block(db, details)
        }
        if (result == Resolution.Done) onQueued()
        return result
    }

    private fun invalid() = Resolution.Invalid("This choice doesn't fit this change.")

    private fun OutboxEntity.isIssue() = state == OutboxState.CONFLICT || state == OutboxState.REJECTED

    /** The op sent again as it is now, from scratch. */
    private suspend fun requeue(db: OfflineDatabase, op: OutboxEntity, payloadJson: String) {
        db.outbox().update(listOf(op.copy(payloadJson = payloadJson, state = OutboxState.PENDING, attempts = 0, nextAttemptAt = 0, lastReason = null, resultJson = null)))
        val id = op.entityId ?: return
        when (entityTypeOf(op.type)) {
            EntityType.REVIEW -> db.reviews().setSyncState(id, SyncState.PENDING)
            EntityType.COLLECTION -> db.collections().setSyncState(id, SyncState.PENDING)
            EntityType.NOTIFICATION -> db.notifications().setSyncState(id, SyncState.PENDING)
        }
    }

    private suspend fun opsOf(db: OfflineDatabase, op: OutboxEntity) = db.outbox().all().filter { it.entityKey() == op.entityKey() }

    /** Later waiting changes of [type] of the same thing (already in the local row the decision used). */
    private suspend fun dropLater(db: OfflineDatabase, op: OutboxEntity, type: String) =
        db.outbox().delete(opsOf(db, op).filter { it.seq > op.seq && it.type == type && it.state == OutboxState.PENDING }.map { it.seq })

    private suspend fun insertCreate(db: OfflineDatabase, type: String, entityType: Int, id: Int, payloadJson: String): OutboxEntity {
        val opId = UUID.randomUUID().toString()
        return insert(db, type, entityType, id, payloadJson, opId = opId, clientRef = opId)
    }

    private suspend fun insert(
        db: OfflineDatabase,
        type: String,
        entityType: Int,
        id: Int,
        payloadJson: String,
        dependsOn: String? = null,
        opId: String = UUID.randomUUID().toString(),
        clientRef: String? = null,
    ): OutboxEntity {
        val op = OutboxEntity(opId = opId, type = type, entityType = entityType, entityId = id, clientRef = clientRef, dependsOn = dependsOn, payloadJson = payloadJson, createdAt = clock())
        return op.copy(seq = db.outbox().insert(op))
    }

    private suspend fun shadow(db: OfflineDatabase, entityType: Int, id: Int, json: String, version: Long?) =
        db.shadows().upsert(listOf(ServerShadowEntity(entityType, id, json, version)))

    private suspend fun collectionNameError(db: OfflineDatabase, name: String, exceptId: Int?): String? = when {
        name.isBlank() -> "Collection name is required."
        name.length > Validation.MAX_COLLECTION_NAME_LENGTH -> "Collection name can be at most ${Validation.MAX_COLLECTION_NAME_LENGTH} characters."
        db.collections().all().any { it.collection.id != exceptId && it.collection.name.equals(name, ignoreCase = true) } ->
            "You already have a collection named \"$name\"."
        else -> null
    }

    private fun reviewFieldsError(fields: ReviewFieldsDto): String? = when {
        (fields.reviewText?.length ?: 0) > Validation.MAX_REVIEW_LENGTH -> "The review can be at most ${Validation.MAX_REVIEW_LENGTH} characters."
        (fields.pros?.length ?: 0) > Validation.MAX_PROS_CONS_LENGTH || (fields.cons?.length ?: 0) > Validation.MAX_PROS_CONS_LENGTH ->
            "Pros and cons can be at most ${Validation.MAX_PROS_CONS_LENGTH} characters."
        else -> null
    }

    private fun <T> SyncOpResultDto.serverEntity(serializer: kotlinx.serialization.KSerializer<T>): T? =
        entity?.takeIf { it is kotlinx.serialization.json.JsonObject }?.let { runCatching { OpJson.decodeFromJsonElement(serializer, it) }.getOrNull() }

    private fun reviewFieldsOf(op: OutboxEntity): ReviewFieldsDto? = runCatching {
        when (op.type) {
            OpTypes.REVIEW_CREATE -> OpJson.decodeFromString(ReviewCreatePayload.serializer(), op.payloadJson).fields
            OpTypes.REVIEW_UPDATE -> OpJson.decodeFromString(ReviewUpdatePayload.serializer(), op.payloadJson).fields
            else -> null
        }
    }.getOrNull()

    private fun collectionFieldsOf(op: OutboxEntity): CollectionFieldsDto? = runCatching {
        when (op.type) {
            OpTypes.COLLECTION_CREATE -> OpJson.decodeFromString(CollectionCreatePayload.serializer(), op.payloadJson).fields
            OpTypes.COLLECTION_UPDATE -> OpJson.decodeFromString(CollectionUpdatePayload.serializer(), op.payloadJson).fields
            else -> null
        }
    }.getOrNull()

    private companion object {
        const val DELETED_HERE = "You deleted it on this phone afterwards, so this can only be discarded."
    }
}

fun ReviewEntity.fields() = ReviewFieldsDto(score, reviewText, pros, cons, visibility)

fun ReviewEntity.withFields(fields: ReviewFieldsDto) =
    copy(score = fields.score, reviewText = fields.reviewText, pros = fields.pros, cons = fields.cons, visibility = fields.visibility)

fun SyncReviewDto.fields() = ReviewFieldsDto(score, reviewText, pros, cons, visibility)
