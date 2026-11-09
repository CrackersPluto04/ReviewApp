package com.reviewapp.android.data.sync

import androidx.room.withTransaction
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.CollectionItemEntity
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.FollowEntity
import com.reviewapp.android.data.local.offline.KnownUserEntity
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.NotificationEntity
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ProfileEntity
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.local.offline.ServerShadowEntity
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionDeletePayload
import com.reviewapp.android.data.remote.dto.CollectionFieldsDto
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.CollectionReorderPayload
import com.reviewapp.android.data.remote.dto.CollectionUpdatePayload
import com.reviewapp.android.data.remote.dto.EntityRefDto
import com.reviewapp.android.data.remote.dto.FollowSetPayload
import com.reviewapp.android.data.remote.dto.FollowerRemovePayload
import com.reviewapp.android.data.remote.dto.MediaKeyDto
import com.reviewapp.android.data.remote.dto.NotificationDeletePayload
import com.reviewapp.android.data.remote.dto.NotificationSeenPayload
import com.reviewapp.android.data.remote.dto.NotificationUpToPayload
import com.reviewapp.android.data.remote.dto.ProfileFieldsDto
import com.reviewapp.android.data.remote.dto.ProfileUpdatePayload
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.data.remote.dto.ReviewDeletePayload
import com.reviewapp.android.data.remote.dto.ReviewFieldsDto
import com.reviewapp.android.data.remote.dto.ReviewUpdatePayload
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncCollectionItemDto
import com.reviewapp.android.data.remote.dto.SyncMediaDto
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.sync.ChangesApplier.toEntity
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.Visibility
import kotlinx.serialization.KSerializer
import java.time.Instant
import java.util.UUID

/** A write the offline copy refuses at once, like the API would. */
class OfflineWriteException(message: String) : Exception(message)

/** How a queued change ended for "Discard". */
enum class DiscardResult { Discarded, Sending, NotFound }

/**
 * The write side of offline mode: each change is applied to the user's offline copy and queued in the outbox in one
 * transaction (combined with waiting changes of the same thing by [OutboxCompactor]), then the sync is asked to run.
 * The screens see the change at once (their reads come from the copy while something waits, see
 * [LocalLibrary.networkFirst]); the sync sends it when it can.
 *
 * The backend's state of a thing is kept as its shadow the first time it is changed locally (`server_shadow`, kept
 * current by pulls), so "Discard" can put it back. Things created here get a negative temporary id until the
 * backend answers; later changes point at them through the create's clientRef (a UUID, unique across devices).
 */
class OfflineWriter(
    private val database: () -> OfflineDatabase?,
    /** Offline mode is active (writes go through here). */
    private val offlineActive: () -> Boolean,
    private val currentUser: () -> AuthUser?,
    /** After every queued change: ask the sync to run (and the OS to run it if the app goes away). */
    private val onQueued: () -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    /** True while offline mode is active: writes go through here instead of the API. */
    fun canWrite(): Boolean = offlineActive() && database() != null

    private fun me(): Int = currentUser()?.id ?: throw OfflineWriteException("You're not logged in.")

    private suspend fun <T> transaction(block: suspend (OfflineDatabase) -> T): T {
        val db = database() ?: throw OfflineWriteException("Offline mode isn't available right now.")
        val result = db.withTransaction { block(db) }
        onQueued()
        return result
    }

    private fun nowIso(): String = Instant.ofEpochMilli(clock()).toString()

    // Reviews

    /** Creates or edits the user's review of [media]; returns the queued op (its result can be awaited). */
    suspend fun saveReview(media: Media, draft: ReviewDraft): OutboxEntity = transaction { db ->
        rememberMedia(db, media)
        val fields = draft.toFields()
        val existing = db.reviews().byMedia(media.mediaType.id, media.externalApiId)
        if (existing == null) {
            val id = nextTempId(db)
            val now = nowIso()
            db.reviews().upsert(
                listOf(
                    ReviewEntity(id, media.mediaType.id, media.externalApiId, fields.score, fields.reviewText, fields.pros, fields.cons,
                        fields.visibility, createdAt = now, updatedAt = now, version = null, syncState = SyncState.PENDING),
                ),
            )
            val payload = ReviewCreatePayload(MediaKeyDto(media.mediaType.id, media.externalApiId), fields, createdAt = now)
            enqueue(db, OpTypes.REVIEW_CREATE, EntityType.REVIEW, id, encode(ReviewCreatePayload.serializer(), payload), isCreate = true)
        } else {
            keepShadow(db, EntityType.REVIEW, existing.id)
            val (ref, dependsOn) = refOf(db, EntityType.REVIEW, existing.id)
            val base = ReviewFieldsDto(existing.score, existing.reviewText, existing.pros, existing.cons, existing.visibility)
            db.reviews().upsert(
                listOf(
                    existing.copy(score = fields.score, reviewText = fields.reviewText, pros = fields.pros, cons = fields.cons,
                        visibility = fields.visibility, updatedAt = nowIso(), syncState = SyncState.PENDING),
                ),
            )
            val payload = ReviewUpdatePayload(ref, baseVersion = existing.version, base = base, fields = fields)
            enqueue(db, OpTypes.REVIEW_UPDATE, EntityType.REVIEW, existing.id, encode(ReviewUpdatePayload.serializer(), payload), dependsOn = dependsOn)
        }
    }

    suspend fun deleteReview(id: Int) = transaction { db ->
        val realId = resolve(db, EntityType.REVIEW, id)
        val review = db.reviews().entity(realId) ?: return@transaction
        keepShadow(db, EntityType.REVIEW, realId)
        val (ref, dependsOn) = refOf(db, EntityType.REVIEW, realId)
        db.reviews().delete(listOf(realId))
        val payload = ReviewDeletePayload(ref, baseVersion = review.version)
        enqueue(db, OpTypes.REVIEW_DELETE, EntityType.REVIEW, realId, encode(ReviewDeletePayload.serializer(), payload), dependsOn = dependsOn)
    }

    // Collections

    suspend fun createCollection(name: String, visibility: Visibility): Pair<CollectionEntity, OutboxEntity> = transaction { db ->
        val trimmed = name.trim()
        checkNameFree(db, trimmed, exceptId = null)
        val id = nextTempId(db)
        val now = nowIso()
        val entity = CollectionEntity(id, trimmed, visibility.id, isDefault = false, createdAt = now, updatedAt = now, version = null, syncState = SyncState.PENDING)
        db.collections().upsert(listOf(entity))
        val payload = CollectionCreatePayload(CollectionFieldsDto(trimmed, visibility.id))
        entity to enqueue(db, OpTypes.COLLECTION_CREATE, EntityType.COLLECTION, id, encode(CollectionCreatePayload.serializer(), payload), isCreate = true)
    }

    suspend fun updateCollection(id: Int, name: String, visibility: Visibility): OutboxEntity = transaction { db ->
        val realId = resolve(db, EntityType.COLLECTION, id)
        val collection = db.collections().entity(realId) ?: throw OfflineWriteException("Collection not found.")
        if (collection.isDefault) throw OfflineWriteException("The Favourites collection can't be edited.")
        val trimmed = name.trim()
        checkNameFree(db, trimmed, exceptId = realId)
        keepShadow(db, EntityType.COLLECTION, realId)
        val (ref, dependsOn) = refOf(db, EntityType.COLLECTION, realId)
        db.collections().upsert(listOf(collection.copy(name = trimmed, visibility = visibility.id, updatedAt = nowIso(), syncState = SyncState.PENDING)))
        val payload = CollectionUpdatePayload(
            collection = ref,
            baseVersion = collection.version,
            base = CollectionFieldsDto(collection.name, collection.visibility),
            fields = CollectionFieldsDto(trimmed, visibility.id),
        )
        enqueue(db, OpTypes.COLLECTION_UPDATE, EntityType.COLLECTION, realId, encode(CollectionUpdatePayload.serializer(), payload), dependsOn = dependsOn)
    }

    suspend fun deleteCollection(id: Int) = transaction { db ->
        val realId = resolve(db, EntityType.COLLECTION, id)
        val collection = db.collections().entity(realId) ?: return@transaction
        if (collection.isDefault) throw OfflineWriteException("The Favourites collection can't be deleted.")
        keepShadow(db, EntityType.COLLECTION, realId)
        val (ref, dependsOn) = refOf(db, EntityType.COLLECTION, realId)
        db.collections().delete(listOf(realId))
        enqueue(db, OpTypes.COLLECTION_DELETE, EntityType.COLLECTION, realId, encode(CollectionDeletePayload.serializer(), CollectionDeletePayload(ref)), dependsOn = dependsOn)
    }

    /** Adds or removes [media] in a collection (the hearts and the add-to-collection sheet). */
    suspend fun setInCollection(collectionId: Int, media: Media, add: Boolean) = transaction { db ->
        val realId = resolve(db, EntityType.COLLECTION, collectionId)
        val collection = db.collections().entity(realId) ?: throw OfflineWriteException("Collection not found.")
        keepShadow(db, EntityType.COLLECTION, realId)
        val items = db.collections().itemsOf(realId)
        val present = items.any { it.mediaType == media.mediaType.id && it.externalApiId == media.externalApiId }
        if (add && !present) {
            rememberMedia(db, media)
            val order = (items.maxOfOrNull { it.orderIndex } ?: -1) + 1
            db.collections().insertItems(listOf(CollectionItemEntity(realId, media.mediaType.id, media.externalApiId, order, nowIso(), SyncState.PENDING)))
        } else if (!add && present) {
            db.collections().deleteItem(realId, media.mediaType.id, media.externalApiId)
        }
        db.collections().upsert(listOf(collection.copy(updatedAt = nowIso(), syncState = SyncState.PENDING)))
        val (ref, dependsOn) = refOf(db, EntityType.COLLECTION, realId)
        val payload = CollectionItemPayload(ref, MediaKeyDto(media.mediaType.id, media.externalApiId))
        val type = if (add) OpTypes.COLLECTION_ADD_ITEM else OpTypes.COLLECTION_REMOVE_ITEM
        enqueue(db, type, EntityType.COLLECTION, realId, encode(CollectionItemPayload.serializer(), payload), dependsOn = dependsOn)
    }

    /** The collection's items in [order] (media of the collection; ones not listed keep their place after them). */
    suspend fun reorder(collectionId: Int, order: List<Media>) = transaction { db ->
        val realId = resolve(db, EntityType.COLLECTION, collectionId)
        val collection = db.collections().entity(realId) ?: throw OfflineWriteException("Collection not found.")
        keepShadow(db, EntityType.COLLECTION, realId)
        val items = db.collections().itemsOf(realId)
        val position = order.withIndex().associate { (index, media) -> (media.mediaType.id to media.externalApiId) to index }
        val reordered = items
            .sortedWith(compareBy<CollectionItemEntity> { position[it.mediaType to it.externalApiId] ?: Int.MAX_VALUE }.thenBy { it.orderIndex })
            .mapIndexed { index, item -> item.copy(orderIndex = index) }
        db.collections().replaceItems(realId, reordered)
        db.collections().upsert(listOf(collection.copy(updatedAt = nowIso(), syncState = SyncState.PENDING)))
        val (ref, dependsOn) = refOf(db, EntityType.COLLECTION, realId)
        val payload = CollectionReorderPayload(ref, reordered.map { MediaKeyDto(it.mediaType, it.externalApiId) })
        enqueue(db, OpTypes.COLLECTION_REORDER, EntityType.COLLECTION, realId, encode(CollectionReorderPayload.serializer(), payload), dependsOn = dependsOn)
    }

    // Profile and follows

    /** A null field stays as it is, an empty one clears it. */
    suspend fun updateProfile(bio: String? = null, profilePictureUrl: String? = null) = transaction { db ->
        val me = me()
        val user = currentUser()
        val profile = db.profile().get(me) ?: ProfileEntity(me, user?.username.orEmpty(), null, user?.profilePictureUrl)
        if (db.shadows().get(EntityType.PROFILE, me) == null) {
            putShadow(db, EntityType.PROFILE, me, encode(ProfileFieldsDto.serializer(), ProfileFieldsDto(profile.bio.orEmpty(), profile.profilePictureUrl.orEmpty())), null)
        }
        val avatar = when {
            profilePictureUrl == null -> profile.profilePictureUrl
            profilePictureUrl.isEmpty() -> null
            else -> profilePictureUrl
        }
        db.profile().upsert(profile.copy(bio = bio ?: profile.bio, profilePictureUrl = avatar, syncState = SyncState.PENDING))
        val payload = ProfileUpdatePayload(
            fields = ProfileFieldsDto(bio = bio, profilePictureUrl = profilePictureUrl),
            base = ProfileFieldsDto(bio = profile.bio.orEmpty(), profilePictureUrl = profile.profilePictureUrl.orEmpty()),
        )
        enqueue(db, OpTypes.PROFILE_UPDATE, EntityType.PROFILE, me, encode(ProfileUpdatePayload.serializer(), payload))
    }

    suspend fun setFollowing(userId: Int, follow: Boolean, username: String?, profilePictureUrl: String?) = transaction { db ->
        val me = me()
        if (userId == me) throw OfflineWriteException("You can't follow yourself.")
        username?.let { db.follows().upsertUsers(listOf(KnownUserEntity(userId, it, profilePictureUrl, clock()))) }
        if (db.shadows().get(EntityType.FOLLOW, userId) == null) {
            putShadow(db, EntityType.FOLLOW, userId, (db.follows().get(me, userId) != null).toString(), null)
        }
        if (follow) db.follows().insert(listOf(FollowEntity(me, userId, SyncState.PENDING))) else db.follows().delete(me, userId)
        enqueue(db, OpTypes.FOLLOW_SET, EntityType.FOLLOW, userId, encode(FollowSetPayload.serializer(), FollowSetPayload(userId, follow)))
    }

    suspend fun removeFollower(userId: Int) = transaction { db ->
        val me = me()
        if (db.shadows().get(EntityType.FOLLOWER, userId) == null) {
            putShadow(db, EntityType.FOLLOWER, userId, (db.follows().get(userId, me) != null).toString(), null)
        }
        db.follows().delete(userId, me)
        enqueue(db, OpTypes.FOLLOWER_REMOVE, EntityType.FOLLOWER, userId, encode(FollowerRemovePayload.serializer(), FollowerRemovePayload(userId)))
    }

    // Notifications

    suspend fun setNotificationSeen(id: Int, seen: Boolean) = transaction { db ->
        keepNotificationShadow(db, id)
        db.notifications().setSeen(id, seen, SyncState.PENDING)
        enqueue(db, OpTypes.NOTIFICATION_SEEN, EntityType.NOTIFICATION, id, encode(NotificationSeenPayload.serializer(), NotificationSeenPayload(id, seen)))
    }

    suspend fun deleteNotification(id: Int) = transaction { db ->
        keepNotificationShadow(db, id)
        db.notifications().markDeleted(id, SyncState.PENDING)
        enqueue(db, OpTypes.NOTIFICATION_DELETE, EntityType.NOTIFICATION, id, encode(NotificationDeletePayload.serializer(), NotificationDeletePayload(id)))
    }

    /** Up to the newest notification on the device, like the API's `upToId`. */
    suspend fun markAllNotificationsSeen() = transaction { db ->
        val upTo = db.notifications().maxId() ?: return@transaction
        db.notifications().markAllSeen(upTo)
        enqueue(db, OpTypes.NOTIFICATION_MARK_ALL_SEEN, EntityType.NOTIFICATION, null, encode(NotificationUpToPayload.serializer(), NotificationUpToPayload(upTo)))
    }

    suspend fun deleteSeenNotifications() = transaction { db ->
        val upTo = db.notifications().maxId() ?: return@transaction
        db.notifications().deleteSeen(upTo)
        enqueue(db, OpTypes.NOTIFICATION_DELETE_SEEN, EntityType.NOTIFICATION, null, encode(NotificationUpToPayload.serializer(), NotificationUpToPayload(upTo)))
    }

    // Discard

    /**
     * Throws away the waiting changes of the thing [seq] changes and puts back the backend's version of it (a thing
     * created offline disappears). Not while one of them is being sent: its answer may already be on the way.
     * [after] runs in the same transaction once the shadow is back (Sync issues put a newer version on top).
     */
    suspend fun discard(seq: Long, after: (suspend (OfflineDatabase) -> Unit)? = null): DiscardResult {
        val db = database() ?: return DiscardResult.NotFound
        val result = db.withTransaction {
            val queue = db.outbox().all()
            val op = queue.firstOrNull { it.seq == seq } ?: return@withTransaction DiscardResult.NotFound
            val ops = queue.filter { it.entityKey() == op.entityKey() }
            if (ops.any { it.state == OutboxState.IN_FLIGHT }) return@withTransaction DiscardResult.Sending
            restore(db, op)
            db.outbox().delete(ops.map { it.seq })
            op.entityId?.let { db.shadows().delete(op.entityType, it) }
            after?.invoke(db)
            DiscardResult.Discarded
        }
        // A bulk change can't be undone locally: the next pull puts the notifications back as they are
        if (result == DiscardResult.Discarded) onQueued()
        return result
    }

    private suspend fun restore(db: OfflineDatabase, op: OutboxEntity) {
        val id = op.entityId ?: return
        val shadow = db.shadows().get(op.entityType, id)
        when (op.entityType) {
            EntityType.REVIEW -> {
                db.reviews().delete(listOf(id))
                shadow?.takeIf { it.json != ServerShadow.DELETED }?.let {
                    db.reviews().upsert(listOf(OpJson.decodeFromString(SyncReviewDto.serializer(), it.json).toEntity()))
                }
            }
            EntityType.COLLECTION -> {
                db.collections().delete(listOf(id))
                shadow?.takeIf { it.json != ServerShadow.DELETED }?.let {
                    val collection = OpJson.decodeFromString(SyncCollectionDto.serializer(), it.json)
                    db.collections().upsert(listOf(collection.toEntity()))
                    db.collections().replaceItems(id, collection.items.map { item ->
                        CollectionItemEntity(id, item.media.mediaType, item.media.externalApiId, item.orderIndex, item.addedAt)
                    })
                }
            }
            EntityType.PROFILE -> shadow?.let {
                val fields = OpJson.decodeFromString(ProfileFieldsDto.serializer(), it.json)
                db.profile().get(id)?.let { profile ->
                    db.profile().upsert(profile.copy(bio = fields.bio, profilePictureUrl = fields.profilePictureUrl?.ifEmpty { null }, syncState = SyncState.SYNCED))
                }
            }
            EntityType.FOLLOW -> shadow?.let {
                if (it.json == "true") db.follows().insert(listOf(FollowEntity(me(), id))) else db.follows().delete(me(), id)
            }
            EntityType.FOLLOWER -> shadow?.let {
                if (it.json == "true") db.follows().insert(listOf(FollowEntity(id, me()))) else db.follows().delete(id, me())
            }
            EntityType.NOTIFICATION -> shadow?.let {
                db.notifications().insert(listOf(OpJson.decodeFromString(NotificationEntity.serializer(), it.json)))
            }
        }
    }

    // Helpers

    private suspend fun enqueue(
        db: OfflineDatabase,
        type: String,
        entityType: Int,
        entityId: Int?,
        payloadJson: String,
        isCreate: Boolean = false,
        dependsOn: List<String> = emptyList(),
    ): OutboxEntity {
        val opId = UUID.randomUUID().toString()
        val op = OutboxEntity(
            opId = opId,
            type = type,
            entityType = entityType,
            entityId = entityId,
            clientRef = if (isCreate) opId else null,
            dependsOn = dependsOn.joinToString(",").ifEmpty { null },
            payloadJson = payloadJson,
            createdAt = clock(),
        )
        val compaction = OutboxCompactor.add(db.outbox().all(), op)
        if (compaction.delete.isNotEmpty()) db.outbox().delete(compaction.delete)
        if (compaction.update.isNotEmpty()) db.outbox().update(compaction.update)
        val inserted = compaction.insert?.let { it.copy(seq = db.outbox().insert(it)) }
        // Folded into an earlier op: that one carries the change now
        return inserted ?: compaction.update.firstOrNull() ?: op
    }

    /** A temporary id resolved to the real one once the backend created the thing. */
    suspend fun resolve(db: OfflineDatabase, entityType: Int, id: Int): Int =
        if (id < 0) db.idMap().realId(entityType, id) ?: id else id

    /** How an op points at an entity: its id, or the clientRef of its still queued create (and depends on it). */
    private suspend fun refOf(db: OfflineDatabase, entityType: Int, id: Int): Pair<EntityRefDto, List<String>> {
        if (id > 0) return EntityRefDto(id = id) to emptyList()
        val createType = if (entityType == EntityType.REVIEW) OpTypes.REVIEW_CREATE else OpTypes.COLLECTION_CREATE
        val create = db.outbox().all().firstOrNull { it.type == createType && it.entityId == id }
            ?: throw OfflineWriteException("This was created offline and is no longer waiting to be sent.")
        return EntityRefDto(ref = create.clientRef) to listOf(create.opId)
    }

    /** The next temporary (negative) id of something created on this phone. */
    suspend fun nextTempId(db: OfflineDatabase): Int {
        val next = db.syncMeta().get(NEXT_TEMP_ID)?.toIntOrNull() ?: -1
        db.syncMeta().put(NEXT_TEMP_ID, (next - 1).toString())
        return next
    }

    private suspend fun checkNameFree(db: OfflineDatabase, name: String, exceptId: Int?) {
        if (name.isBlank()) throw OfflineWriteException("Collection name is required.")
        val taken = db.collections().all().any { it.collection.id != exceptId && it.collection.name.equals(name, ignoreCase = true) }
        if (taken) throw OfflineWriteException("You already have a collection named \"$name\".")
    }

    private suspend fun rememberMedia(db: OfflineDatabase, media: Media) {
        val existing = db.media().get(media.mediaType.id, media.externalApiId)
        db.media().upsert(
            listOf(
                MediaEntity(media.mediaType.id, media.externalApiId, existing?.dbMediaId, media.title.ifBlank { existing?.title },
                    media.posterUrl ?: existing?.posterUrl, media.releaseDate ?: existing?.releaseDate,
                    media.creator.ifBlank { existing?.creator }, clock()),
            ),
        )
    }

    /** The backend's version of a review / collection before its first local change (only for ones it knows). */
    private suspend fun keepShadow(db: OfflineDatabase, entityType: Int, id: Int) {
        if (id < 0 || db.shadows().get(entityType, id) != null) return
        when (entityType) {
            EntityType.REVIEW -> {
                val review = db.reviews().entity(id) ?: return
                putShadow(db, entityType, id, encode(SyncReviewDto.serializer(), review.toSync(db)), review.version)
            }
            EntityType.COLLECTION -> {
                val collection = db.collections().entity(id) ?: return
                putShadow(db, entityType, id, encode(SyncCollectionDto.serializer(), collection.toSync(db)), collection.version)
            }
        }
    }

    private suspend fun keepNotificationShadow(db: OfflineDatabase, id: Int) {
        if (db.shadows().get(EntityType.NOTIFICATION, id) != null) return
        val notification = db.notifications().get(id) ?: throw OfflineWriteException("Notification not found.")
        putShadow(db, EntityType.NOTIFICATION, id, encode(NotificationEntity.serializer(), notification.copy(syncState = SyncState.SYNCED)), null)
    }

    private suspend fun putShadow(db: OfflineDatabase, entityType: Int, id: Int, json: String, version: Long?) =
        db.shadows().upsert(listOf(ServerShadowEntity(entityType, id, json, version)))

    private suspend fun ReviewEntity.toSync(db: OfflineDatabase): SyncReviewDto {
        val media = db.media().get(mediaType, externalApiId)
        return SyncReviewDto(id, media.toSync(mediaType, externalApiId), score, reviewText, pros, cons, visibility, createdAt, updatedAt, version ?: 0)
    }

    private suspend fun CollectionEntity.toSync(db: OfflineDatabase): SyncCollectionDto {
        val items = db.collections().itemsOf(id).map { item ->
            SyncCollectionItemDto(db.media().get(item.mediaType, item.externalApiId).toSync(item.mediaType, item.externalApiId), item.orderIndex, item.addedAt)
        }
        return SyncCollectionDto(id, name, visibility, isDefault, createdAt, updatedAt, version ?: 0, items)
    }

    private fun MediaEntity?.toSync(type: Int, externalApiId: String) =
        SyncMediaDto(this?.dbMediaId ?: 0, type, externalApiId, this?.title, this?.posterUrl, this?.releaseDate, this?.creator)

    private fun <T> encode(serializer: KSerializer<T>, value: T): String = OpJson.encodeToString(serializer, value)

    private companion object {
        const val NEXT_TEMP_ID = "next_temp_id"
    }
}

/** What the repositories need for offline writes: the writer, and the engine to await an op's answer. */
class OfflineWrites(val writer: OfflineWriter, val engine: SyncEngine)

/** The API's review fields of a form draft (blank texts are no text, like the online form sends them). */
fun ReviewDraft.toFields() = ReviewFieldsDto(
    score = score,
    reviewText = reviewText.trim().ifEmpty { null },
    pros = pros.trim().ifEmpty { null },
    cons = cons.trim().ifEmpty { null },
    visibility = visibility.id,
)
