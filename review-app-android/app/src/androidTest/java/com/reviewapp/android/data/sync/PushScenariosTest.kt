package com.reviewapp.android.data.sync

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.reviewapp.android.data.local.offline.EntityType
import com.reviewapp.android.data.local.offline.OfflineDatabase
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.local.offline.ReviewEntity
import com.reviewapp.android.data.local.offline.SyncState
import com.reviewapp.android.data.remote.SyncApi
import com.reviewapp.android.data.remote.dto.SyncChangesDto
import com.reviewapp.android.data.remote.dto.SyncOpDto
import com.reviewapp.android.data.remote.dto.SyncOpResultDto
import com.reviewapp.android.data.remote.dto.SyncPushRequest
import com.reviewapp.android.data.remote.dto.SyncPushResponse
import com.reviewapp.android.data.remote.dto.SyncVisibleDto
import com.reviewapp.android.data.remote.dto.SyncVisibleRequest
import com.reviewapp.android.domain.model.AuthUser
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.domain.model.ReviewDraft
import com.reviewapp.android.domain.model.Visibility
import kotlinx.coroutines.runBlocking
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.HttpException
import retrofit2.Response
import java.io.File
import java.io.IOException
import java.security.SecureRandom

/**
 * The push against a scripted backend on the real (encrypted) database: the failure scenarios of the spec. The fake
 * backend behaves like the real one where it matters: it remembers applied op ids and answers them again, skips ops
 * whose dependency wasn't applied, and gives creates their ids.
 */
@RunWith(AndroidJUnit4::class)
class PushScenariosTest {

    private class FakeBackend : SyncApi {
        val applied = HashMap<String, SyncOpResultDto>()
        val applyCount = HashMap<String, Int>()
        var calls = 0
        private var nextId = 500

        var failBeforeApply = false
        /** Applies everything, then the answer is lost (the connection drops on the way back). */
        var loseResponse = false
        var httpError: Int? = null
        var retryAfterSeconds: Long? = null
        /** From this call on (1-based) every call fails without an answer. */
        var dropFromCall: Int? = null
        /** A scripted answer for an op (not stored, like a conflict or a rejection). */
        var answerFor: (SyncOpDto) -> SyncOpResultDto? = { null }

        override suspend fun push(body: SyncPushRequest): SyncPushResponse {
            calls++
            if (failBeforeApply || (dropFromCall != null && calls >= dropFromCall!!)) throw IOException("offline")
            httpError?.let { code ->
                val raw = okhttp3.Response.Builder()
                    .request(Request.Builder().url("https://localhost/api/Sync/push").build())
                    .protocol(Protocol.HTTP_1_1).code(code).message("error")
                    .apply { retryAfterSeconds?.let { header("Retry-After", it.toString()) } }
                    .build()
                throw HttpException(Response.error<Any>("{}".toResponseBody(), raw))
            }
            val results = body.ops.map { op ->
                applied[op.opId] ?: run {
                    val dependencyMissing = op.dependsOn.orEmpty().any { dep -> applied[dep] == null }
                    when {
                        dependencyMissing -> SyncOpResultDto(op.opId, "skipped", "dependency")
                        else -> answerFor(op) ?: run {
                            applyCount[op.opId] = (applyCount[op.opId] ?: 0) + 1
                            val id = if (op.type.endsWith(".create")) nextId++ else null
                            SyncOpResultDto(op.opId, "applied", entityId = id).also { applied[op.opId] = it }
                        }
                    }
                }
            }
            if (loseResponse) {
                loseResponse = false
                throw IOException("answer lost")
            }
            return SyncPushResponse(results)
        }

        override suspend fun changes(since: Long, syncedAt: String?, continued: Boolean, limit: Int): SyncChangesDto = throw UnsupportedOperationException()
        override suspend fun visible(body: SyncVisibleRequest): SyncVisibleDto = throw UnsupportedOperationException()
    }

    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val file = File(context.cacheDir, "push_${System.nanoTime()}.db")
    private val db = OfflineDatabase.open(context, file, ByteArray(32).also { SecureRandom().nextBytes(it) })
    private val backend = FakeBackend()
    private var now = 1_000_000L
    private val me = AuthUser(7, "me", null, isEmailConfirmed = true)
    private val writer = OfflineWriter(database = { db }, offlineActive = { true }, currentUser = { me }, onQueued = {}, clock = { now })
    private val push = PushRunner(backend, userId = { 7 }, onUnlocked = {}, clock = { now }, random = { 0.5 })

    private val dune = Media("438631", MediaType.Movie, "Dune", "2021-09-15", null, "", "Denis Villeneuve")
    private fun draft(score: Double) = ReviewDraft(score, "Text", "", "", Visibility.Public)

    @After
    fun cleanUp() {
        db.close()
        listOf("", "-wal", "-shm", "-journal").forEach { File(file.path + it).delete() }
    }

    @Test
    fun aReviewCreatedOffline_GetsItsRealIdEverywhere() = runBlocking {
        writer.saveReview(dune, draft(8.0))

        assertEquals(PushOutcome.Done(applied = 1, issues = 0), push.push(db))

        val review = db.reviews().byMedia(0, "438631")!!
        assertEquals(500, review.id)
        assertEquals(SyncState.SYNCED, review.syncState)
        assertEquals(500, db.idMap().realId(EntityType.REVIEW, -1))
        assertEquals(0, db.outbox().count())
    }

    @Test
    fun aLostAnswer_TheRetryIsRecognised_AndAppliedOnce() = runBlocking {
        val op = writer.saveReview(dune, draft(8.0))
        backend.loseResponse = true

        assertEquals(PushOutcome.Offline, push.push(db))
        assertEquals(OutboxState.PENDING, db.outbox().all().single().state)

        assertEquals(PushOutcome.Done(1, 0), push.push(db))
        assertEquals(1, backend.applyCount[op.opId])
        assertEquals(500, db.reviews().byMedia(0, "438631")!!.id)
    }

    @Test
    fun theConnectionDropsBetweenBatches_TheRestWaits_NothingRepeats() = runBlocking {
        (1..60).forEach { writer.setFollowing(100 + it, follow = true, username = "user$it", profilePictureUrl = null) }
        backend.dropFromCall = 2

        assertEquals(PushOutcome.Offline, push.push(db))
        assertEquals(10, db.outbox().count())

        backend.dropFromCall = null
        assertEquals(PushOutcome.Done(10, 0), push.push(db))
        assertEquals(0, db.outbox().count())
        assertTrue(backend.applyCount.values.all { it == 1 })
        assertEquals(60, backend.applyCount.size)
    }

    @Test
    fun aRevokedSession_KeepsEverythingQueued() = runBlocking {
        writer.saveReview(dune, draft(8.0))
        writer.updateProfile(bio = "Offline bio")
        backend.httpError = 401

        assertEquals(PushOutcome.NeedsLogin, push.push(db))

        val queue = db.outbox().all()
        assertEquals(2, queue.size)
        assertTrue(queue.all { it.state == OutboxState.PENDING && it.attempts == 0 })
    }

    @Test
    fun tooManyRequests_PausesUntilTheBackendSays() = runBlocking {
        writer.saveReview(dune, draft(8.0))
        backend.httpError = 429
        backend.retryAfterSeconds = 30

        val paused = push.push(db) as PushOutcome.Paused

        assertTrue(paused.untilMillis >= now + 30_000)
        assertTrue(SyncPlanner.nextBatch(db.outbox().all(), now + 1_000).isEmpty())
        assertEquals(1, SyncPlanner.nextBatch(db.outbox().all(), paused.untilMillis).size)
    }

    @Test
    fun aConflictInADependentChain_TheDependentsWait_AndAreNotSentOnAWrongBase() = runBlocking {
        val (collection, create) = writer.createCollection("Trip", Visibility.Private)
        writer.setInCollection(collection.id, dune, add = true)
        backend.answerFor = { op -> if (op.opId == create.opId) SyncOpResultDto(op.opId, "conflict", "name_taken") else null }

        assertEquals(PushOutcome.Done(applied = 0, issues = 1), push.push(db))

        val queue = db.outbox().all()
        assertEquals(OutboxState.CONFLICT, queue.first { it.opId == create.opId }.state)
        assertEquals(OutboxState.PENDING, queue.first { it.type == OpTypes.COLLECTION_ADD_ITEM }.state)
        assertEquals(SyncState.ISSUE, db.collections().entity(collection.id)!!.syncState)

        // Nothing more goes until the user decides
        val calls = backend.calls
        assertEquals(PushOutcome.Done(0, 0), push.push(db))
        assertEquals(calls, backend.calls)
    }

    @Test
    fun theAppKilledWhileSending_TheOpIsSentAgain_AndRecognised() = runBlocking {
        val op = writer.saveReview(dune, draft(8.0))
        // The backend applied it, but the app died before the answer was stored
        backend.push(SyncPushRequest(7, listOf(SyncOpDto(op.opId, op.type, op.clientRef, null, OpJson.parseToJsonElement(op.payloadJson) as kotlinx.serialization.json.JsonObject))))
        db.outbox().update(listOf(db.outbox().all().single().copy(state = OutboxState.IN_FLIGHT)))

        push.recover(db)
        assertEquals(PushOutcome.Done(1, 0), push.push(db))

        assertEquals(1, backend.applyCount[op.opId])
        assertEquals(500, db.reviews().byMedia(0, "438631")!!.id)
    }

    @Test
    fun laterChangesOfSomethingCreatedOffline_PointAtItByItsClientRef() = runBlocking {
        val (collection, create) = writer.createCollection("Trip", Visibility.Private)
        writer.setInCollection(collection.id, dune, add = true)

        val add = db.outbox().all().first { it.type == OpTypes.COLLECTION_ADD_ITEM }
        assertEquals(create.opId, add.dependsOn)
        assertTrue(add.payloadJson.contains("\"ref\":\"${create.clientRef}\""))

        assertEquals(PushOutcome.Done(2, 0), push.push(db))
        assertEquals(500, db.idMap().realId(EntityType.COLLECTION, collection.id))
        assertEquals(listOf("438631"), db.collections().itemsOf(500).map { it.externalApiId })
    }

    @Test
    fun discard_PutsBackTheBackendsVersion() = runBlocking {
        db.reviews().upsert(listOf(ReviewEntity(42, 0, "438631", 6.0, "Old", null, null, 1, "2026-11-01T00:00:00Z", "2026-11-01T00:00:00Z", version = 9)))
        val edit = writer.saveReview(dune, draft(9.0))
        assertEquals(SyncState.PENDING, db.reviews().entity(42)!!.syncState)

        assertEquals(DiscardResult.Discarded, writer.discard(edit.seq))

        val review = db.reviews().entity(42)!!
        assertEquals(6.0, review.score, 0.0)
        assertEquals("Old", review.reviewText)
        assertEquals(SyncState.SYNCED, review.syncState)
        assertEquals(0, db.outbox().count())
        assertNull(db.shadows().get(EntityType.REVIEW, 42))
    }

    @Test
    fun discard_OfSomethingCreatedOffline_RemovesIt() = runBlocking {
        val create = writer.saveReview(dune, draft(8.0))

        assertEquals(DiscardResult.Discarded, writer.discard(create.seq))

        assertNull(db.reviews().byMedia(0, "438631"))
        assertEquals(0, db.outbox().count())
    }

    @Test
    fun discard_WhileSending_IsRefused() = runBlocking {
        val create = writer.saveReview(dune, draft(8.0))
        db.outbox().update(listOf(db.outbox().all().single().copy(state = OutboxState.IN_FLIGHT)))

        assertEquals(DiscardResult.Sending, writer.discard(create.seq))
    }

    @Test
    fun anEditUpdate_CarriesWhatTheUserStartedFrom() = runBlocking {
        db.reviews().upsert(listOf(ReviewEntity(42, 0, "438631", 6.0, "Old", null, null, 1, "", "", version = 9)))

        writer.saveReview(dune, draft(9.0))

        val payload = OpJson.decodeFromString(com.reviewapp.android.data.remote.dto.ReviewUpdatePayload.serializer(), db.outbox().all().single().payloadJson)
        assertEquals(9L, payload.baseVersion)
        assertEquals(6.0, payload.base!!.score, 0.0)
        assertEquals(9.0, payload.fields.score, 0.0)
        assertEquals(42, payload.review.id)
    }

    @Test
    fun aNameTheCopyAlreadyHas_IsRefusedAtOnce() = runBlocking {
        writer.createCollection("Trip", Visibility.Private)

        val error = runCatching { writer.createCollection("trip", Visibility.Public) }.exceptionOrNull()

        assertTrue(error is OfflineWriteException)
        assertEquals(1, db.outbox().count())
    }
}
