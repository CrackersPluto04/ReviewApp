package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.SecureRandom

// Room on SQLCipher on a real device: data survives a reopen with the key, the file is not plain SQLite,
// and a wrong key can't read it
@RunWith(AndroidJUnit4::class)
class OfflineDatabaseTest {

    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val file = File(context.cacheDir, "offline_test_${System.nanoTime()}.db")
    private val key = ByteArray(32).also { SecureRandom().nextBytes(it) }

    @After
    fun cleanUp() {
        listOf("", "-wal", "-shm", "-journal").forEach { File(file.path + it).delete() }
    }

    @Test
    fun dataSurvivesAReopenWithTheKey_AndTheFileIsEncrypted() = runBlocking {
        OfflineDatabase.open(context, file, key.copyOf()).apply {
            syncMeta().put(SyncMetaKeys.CURSOR, "42")
            close()
        }

        val reopened = OfflineDatabase.open(context, file, key.copyOf())
        assertEquals("42", reopened.syncMeta().get(SyncMetaKeys.CURSOR))
        reopened.close()

        val header = ByteArray(16).also { bytes -> file.inputStream().use { it.read(bytes) } }
        assertFalse("plain SQLite header", String(header, Charsets.US_ASCII).startsWith("SQLite format 3"))
    }

    @Test
    fun aWrongKey_CantReadIt() {
        runBlocking {
            OfflineDatabase.open(context, file, key.copyOf()).apply {
                syncMeta().put(SyncMetaKeys.CURSOR, "1")
                close()
            }
        }

        val wrong = ByteArray(32).also { SecureRandom().nextBytes(it) }
        val failed = runCatching {
            runBlocking { OfflineDatabase.open(context, file, wrong).run { syncMeta().get(SyncMetaKeys.CURSOR).also { close() } } }
        }.isFailure
        assertTrue("a wrong key opened the database", failed)
    }
}
