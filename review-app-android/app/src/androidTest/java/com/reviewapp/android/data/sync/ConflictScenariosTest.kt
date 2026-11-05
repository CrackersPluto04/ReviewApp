package com.reviewapp.android.data.sync

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.reviewapp.android.data.local.offline.CollectionEntity
import com.reviewapp.android.data.local.offline.CollectionItemEntity
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.MediaEntity
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.data.remote.SyncApi
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.ReviewDeletePayload
import com.reviewapp.android.data.remote.dto.ReviewUpdatePayload
import com.reviewapp.android.data.remote.dto.SyncChangesDto
import com.reviewapp.android.data.remote.dto.SyncCollectionDto
import com.reviewapp.android.data.remote.dto.SyncCollectionItemDto
import com.reviewapp.android.data.remote.dto.SyncMediaDto
import com.reviewapp.android.data.remote.dto.SyncOpDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.data.remote.dto.SyncPushRequest
import com.reviewapp.android.data.remote.dto.SyncPushResponse
import com.reviewapp.android.data.remote.dto.SyncReviewDto
import com.reviewapp.android.data.remote.dto.SyncVisibleDto
import com.reviewapp.android.data.remote.dto.SyncVisibleRequest
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.Visibility
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.SecureRandom

/**
 * Sync issues end to end on the real (encrypted) database: a change runs into a conflict, the user decides, and the
 * change sent again is applied. The fake backend answers each op with its script once (a conflict is never stored,
 * like on the real one), then applies it.
 */
@RunWith(AndroidJUnit4::class)
class ConflictScenariosTest {

    private class FakeBackend : SyncApi {
        val sent = ArrayList<SyncOpDto>()
        val applied = HashMap<String, SyncOpResultDto>()
        private var nextId = 500
        /** The scripted answer of the next op of a type (used once). */
        private val scripted = HashMap<String, (String) -> SyncOpResultDto>()

        fun answer(type: String, result: (opId: String) -> SyncOpResultDto) {
            scripted[type] = result
        }

        override suspend fun push(body: SyncPushRequest): SyncPushResponse = SyncPushResponse(
            body.ops.map { op ->
                sent += op
                applied[op.opId] ?: run {
                    val scriptedAnswer = scripted.remove(op.type)
                    when {
                        op.dependsOn.orEmpty().any { applied[it] == null } -> SyncOpResultDto(op.opId, "skipped", "dependency")
                        scriptedAnswer != null -> scriptedAnswer(op.opId)
                        else -> SyncOpResultDto(op.opId, "applied", entityId = if (op.type.endsWith(".create")) nextId++ else null).also { applied[op.opId] = it }
                    }
                }
            },
        )

        override suspend fun changes(since: Long, syncedAt: String?, continued: Boolean, limit: Int): SyncChangesDto = throw UnsupportedOperationException()
        override suspend fun visible(body: SyncVisibleRequest): SyncVisibleDto = throw UnsupportedOperationException()
    }

    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val file = File(context.cacheDir, "conflict_${System.nanoTime()}.db")
    private val db = OfflineDatabase.open(context, file, ByteArray(32).also { SecureRandom().nextBytes(it) })
    private val backend = FakeBackend()
    private val me = AuthUser(7, "me", null, isEmailConfirmed = true)
    private val writer = OfflineWriter(database = { db }, offlineActive = { true }, currentUser = { me }, onQueued = {})
    private val resolver = ConflictResolver(database = { db }, writer = writer, onQueued = {})
    private val push = PushRunner(backend, userId = { 7 }, onUnlocked = {}, random = { 0.5 })

    private val dune = Media("438631", MediaType.Movie, "Dune", "2021-09-15", null, "", "Denis Villeneuve")
    private val inception = Media("27205", MediaType.Movie, "Inception", "2010-07-15", null, "", "Christopher Nolan")
    private val duneDto = SyncMediaDto(11, 0, "438631", "Dune")

    @After
    fun cleanUp() {
        db.close()
        listOf("", "-wal", "-shm", "-journal").forEach { File(file.path + it).delete() }
    }

    private suspend fun syncedReview(text: String = "Good", version: Long = 30) {
        db.media().upsert(listOf(MediaEntity(0, "438631", 11, "Dune", null, null, null, 0)))
        db.reviews().upsert(listOf(ReviewEntity(12, 0, "438631", 7.0, text, null, null, 1, "2026-10-01T10:00:00Z", "2026-10-01T10:00:00Z", version)))
    }

    private suspend fun issue() = db.outbox().all().single { it.state == OutboxState.CONFLICT || it.state == OutboxState.REJECTED }

    @Test
    fun aFieldConflict_TheirsForTheText_MineForTheScore_IsSentOnTopOfTheirVersion() = runBlocking {
        syncedReview()
        writer.saveReview(dune, ReviewDraft(9.0, "Loved it", "", "", Visibility.Public))
        val server = SyncReviewDto(12, duneDto, 7.0, "Too long", version = 40)
        backend.answer(OpTypes.REVIEW_UPDATE) { SyncOpResultDto(it, "conflict", "field_conflict", 12, OpJson.encodeToJsonElement(SyncReviewDto.serializer(), server), conflictFields = listOf("reviewText")) }

        assertEquals(PushOutcome.Done(applied = 0, issues = 1), push.push(db))
        assertEquals(SyncState.ISSUE, db.reviews().entity(12)!!.syncState)

        assertEquals(Resolution.Done, resolver.resolveFields(issue().seq, mapOf("reviewText" to FieldChoice.Theirs)))
        val requeued = db.outbox().all().single()
        val payload = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), requeued.payloadJson)
        assertEquals(40L, payload.baseVersion)
        assertEquals("Too long", payload.fields.reviewText)
        assertEquals(9.0, payload.fields.score, 0.0)
        assertEquals("Too long", db.reviews().entity(12)!!.reviewText)

        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertEquals(0, db.outbox().count())
        assertEquals(SyncState.SYNCED, db.reviews().entity(12)!!.syncState)
    }

    @Test
    fun alreadyReviewedElsewhere_KeepMine_BecomesAnEditOfThatReview() = runBlocking {
        val create = writer.saveReview(dune, ReviewDraft(9.0, "Mine", "", "", Visibility.Public))
        val server = SyncReviewDto(77, duneDto, 5.0, "Theirs", version = 50)
        backend.answer(OpTypes.REVIEW_CREATE) { SyncOpResultDto(it, "conflict", "already_reviewed", 77, OpJson.encodeToJsonElement(SyncReviewDto.serializer(), server)) }
        // Edited again while it was in Sync issues
        push.push(db)
        writer.saveReview(dune, ReviewDraft(9.5, "Mine, edited", "", "", Visibility.Public))

        assertEquals(Resolution.Done, resolver.overwriteWithMine(issue().seq))

        val review = db.reviews().byMedia(0, "438631")!!
        assertEquals(77, review.id)
        assertEquals(77, db.idMap().realId(EntityType.REVIEW, create.entityId!!))
        val update = db.outbox().all().single()
        assertEquals(OpTypes.REVIEW_UPDATE, update.type)
        val payload = OpJson.decodeFromString(ReviewUpdatePayload.serializer(), update.payloadJson)
        assertEquals(77, payload.review.id)
        assertEquals(50L, payload.baseVersion)
        assertEquals("Mine, edited", payload.fields.reviewText)

        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertEquals(0, db.outbox().count())
    }

    @Test
    fun alreadyReviewedElsewhere_UseTheirs_ShowsThatReview() = runBlocking {
        writer.saveReview(dune, ReviewDraft(9.0, "Mine", "", "", Visibility.Public))
        val server = SyncReviewDto(77, duneDto, 5.0, "Theirs", version = 50)
        backend.answer(OpTypes.REVIEW_CREATE) { SyncOpResultDto(it, "conflict", "already_reviewed", 77, OpJson.encodeToJsonElement(SyncReviewDto.serializer(), server)) }
        push.push(db)

        assertEquals(Resolution.Done, resolver.useTheirs(issue().seq))

        assertEquals(0, db.outbox().count())
        val review = db.reviews().byMedia(0, "438631")!!
        assertEquals(77, review.id)
        assertEquals("Theirs", review.reviewText)
    }

    @Test
    fun aNameTakenCollection_MergedIntoTheExistingOne_ItsItemsGoThere() = runBlocking {
        val (created, _) = writer.createCollection("Watch later", Visibility.Public)
        writer.setInCollection(created.id, dune, add = true)
        val existing = SyncCollectionDto(30, "Watch later", 1, version = 60, items = listOf(SyncCollectionItemDto(SyncMediaDto(12, 0, "27205", "Inception"), 0)))
        backend.answer(OpTypes.COLLECTION_CREATE) { SyncOpResultDto(it, "conflict", "name_taken", 30, OpJson.encodeToJsonElement(SyncCollectionDto.serializer(), existing)) }

        assertEquals(PushOutcome.Done(applied = 0, issues = 1), push.push(db))
        // The addition waits for the create
        assertEquals(OutboxState.PENDING, db.outbox().all().single { it.type == OpTypes.COLLECTION_ADD_ITEM }.state)

        assertEquals(Resolution.Done, resolver.mergeIntoExisting(issue().seq))

        assertNull(db.collections().entity(created.id))
        assertEquals(listOf("27205", "438631"), db.collections().itemsOf(30).map { it.externalApiId })
        assertEquals(30, db.idMap().realId(EntityType.COLLECTION, created.id))
        val add = db.outbox().all().single()
        assertEquals(30, OpJson.decodeFromString(CollectionItemPayload.serializer(), add.payloadJson).collection.id)
        assertNull(add.dependsOn)

        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertEquals(0, db.outbox().count())
    }

    @Test
    fun aNameTakenCollection_Renamed_IsCreated_AndItsItemsFollow() = runBlocking {
        val (created, _) = writer.createCollection("Watch later", Visibility.Public)
        writer.setInCollection(created.id, dune, add = true)
        backend.answer(OpTypes.COLLECTION_CREATE) { SyncOpResultDto(it, "conflict", "name_taken", 30) }
        push.push(db)

        assertTrue(resolver.rename(issue().seq, " ") is Resolution.Invalid)
        assertEquals(Resolution.Done, resolver.rename(issue().seq, "Watch on the plane"))

        assertEquals(PushOutcome.Done(2, 0), push.push(db))
        val collection = db.collections().entity(500)!!
        assertEquals("Watch on the plane", collection.name)
        assertEquals(listOf("438631"), db.collections().itemsOf(500).map { it.externalApiId })
    }

    @Test
    fun aCollectionDeletedElsewhere_Recreated_WithEverythingThisPhoneShows() = runBlocking {
        db.media().upsert(listOf(MediaEntity(0, "438631", 11, "Dune", null, null, null, 0)))
        db.collections().upsert(listOf(CollectionEntity(8, "Trip", 1, false, "", "", 70)))
        db.collections().insertItems(listOf(CollectionItemEntity(8, 0, "438631", 0, "")))
        writer.setInCollection(8, inception, add = true)
        backend.answer(OpTypes.COLLECTION_ADD_ITEM) { SyncOpResultDto(it, "conflict", "collection_deleted", 8) }
        push.push(db)

        assertEquals(Resolution.Done, resolver.recreate(issue().seq))

        val queue = db.outbox().all()
        assertEquals(listOf(OpTypes.COLLECTION_CREATE, OpTypes.COLLECTION_ADD_ITEM, OpTypes.COLLECTION_ADD_ITEM), queue.map { it.type })
        assertTrue(queue.drop(1).all { it.dependsOn == queue[0].opId })

        assertEquals(PushOutcome.Done(3, 0), push.push(db))
        assertNull(db.collections().entity(8))
        assertEquals(listOf("438631", "27205"), db.collections().itemsOf(500).map { it.externalApiId })
        assertEquals(SyncState.SYNCED, db.collections().entity(500)!!.syncState)
    }

    @Test
    fun aReviewDeletedHere_EditedElsewhereSince_DeleteAnyway() = runBlocking {
        syncedReview()
        writer.deleteReview(12)
        val server = SyncReviewDto(12, duneDto, 7.0, "Edited on the web", version = 41)
        backend.answer(OpTypes.REVIEW_DELETE) { SyncOpResultDto(it, "conflict", "modified_since", 12, OpJson.encodeToJsonElement(SyncReviewDto.serializer(), server)) }
        push.push(db)

        assertEquals(Resolution.Done, resolver.deleteAnyway(issue().seq))
        assertEquals(41L, OpJson.decodeFromString(ReviewDeletePayload.serializer(), db.outbox().all().single().payloadJson).baseVersion)

        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertNull(db.reviews().entity(12))
    }

    @Test
    fun aReviewDeletedHere_EditedElsewhereSince_KeepIt_ShowsTheEditedOne() = runBlocking {
        syncedReview()
        writer.deleteReview(12)
        val server = SyncReviewDto(12, duneDto, 7.0, "Edited on the web", version = 41)
        backend.answer(OpTypes.REVIEW_DELETE) { SyncOpResultDto(it, "conflict", "modified_since", 12, OpJson.encodeToJsonElement(SyncReviewDto.serializer(), server)) }
        push.push(db)

        assertEquals(Resolution.Done, resolver.useTheirs(issue().seq))

        assertEquals(0, db.outbox().count())
        assertEquals("Edited on the web", db.reviews().entity(12)!!.reviewText)
    }

    @Test
    fun aRejectionOfAFailingServer_TriedAgain_GoesThrough() = runBlocking {
        syncedReview()
        writer.saveReview(dune, ReviewDraft(8.0, "Again", "", "", Visibility.Public))
        backend.answer(OpTypes.REVIEW_UPDATE) { SyncOpResultDto(it, "rejected", "server_error") }
        push.push(db)
        assertEquals(OutboxState.REJECTED, issue().state)

        assertEquals(Resolution.Done, resolver.retry(issue().seq))
        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertEquals(0, db.outbox().count())
    }

    @Test
    fun aDecisionOnAChangeThatIsGone_SaysSo() = runBlocking {
        assertEquals(Resolution.Gone, resolver.recreate(999))
        assertEquals(Resolution.Gone, resolver.useTheirs(999))
    }
}
