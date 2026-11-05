package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.reviewapp.android.data.local.CryptoStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

// The per-user offline databases: one file per user, at most one open, a lost key starts a new copy
@RunWith(AndroidJUnit4::class)
class DatabaseManagerTest {

    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val storeFile = File(context.cacheDir, "keys_${System.nanoTime()}.preferences_pb")
    private val dataStore = PreferenceDataStoreFactory.create(scope = scope, produceFile = { storeFile })
    private val manager = DatabaseManager(context, DatabaseKeyStore(dataStore, CryptoStore("review_app_db_key_test")))
    // Far from real user ids, so a developer's own offline copy is never touched
    private val alice = 900_000_001
    private val bob = 900_000_002

    @After
    fun cleanUp() {
        runBlocking {
            manager.delete(alice)
            manager.delete(bob)
        }
        scope.cancel()
        storeFile.delete()
    }

    @Test
    fun eachUserHasTheirOwnFile_AndOnlyOneIsOpen() = runBlocking {
        manager.open(alice).syncMeta().put(SyncMetaKeys.CURSOR, "alice")
        val bobs = manager.open(bob)

        assertNull(bobs.syncMeta().get(SyncMetaKeys.CURSOR))
        assertEquals(bob, manager.current.value?.userId)
        assertFalse("alice's database stays open", manager.open(alice) === bobs)
        assertEquals("alice", manager.open(alice).syncMeta().get(SyncMetaKeys.CURSOR))
        assertTrue(manager.exists(alice) && manager.exists(bob))
        assertTrue(manager.sizeBytes(alice) > 0)
    }

    @Test
    fun theDataSurvivesClosing() = runBlocking {
        manager.open(alice).syncMeta().put(SyncMetaKeys.CURSOR, "7")
        manager.close()

        assertNull(manager.current.value)
        assertEquals("7", manager.open(alice).syncMeta().get(SyncMetaKeys.CURSOR))
    }

    @Test
    fun aLostKey_StartsANewEmptyCopy_InsteadOfFailing() = runBlocking {
        manager.open(alice).syncMeta().put(SyncMetaKeys.CURSOR, "7")
        manager.close()
        // The wrapped key can't be decrypted any more (as after a restore onto another device)
        dataStore.edit { it[stringPreferencesKey("offline_db_key_$alice")] = "not-a-wrapped-key" }

        val fresh = manager.open(alice)

        assertNull(fresh.syncMeta().get(SyncMetaKeys.CURSOR))
        fresh.syncMeta().put(SyncMetaKeys.CURSOR, "8")
        manager.close()
        assertEquals("8", manager.open(alice).syncMeta().get(SyncMetaKeys.CURSOR))
    }

    @Test
    fun delete_RemovesTheFileAndCloses() = runBlocking {
        manager.open(alice).syncMeta().put(SyncMetaKeys.CURSOR, "7")

        manager.delete(alice)

        assertFalse(manager.exists(alice))
        assertNull(manager.current.value)
        assertNull(manager.open(alice).syncMeta().get(SyncMetaKeys.CURSOR))
    }
}
